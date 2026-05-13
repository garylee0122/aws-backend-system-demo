<?php

namespace App\Jobs;

use App\Models\Order;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Queue\Queueable;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\Facades\Redis;
use JsonException;
use Throwable;

class SendOrderJob implements ShouldQueue
{
    use Queueable;

    private const QUEUE_NAME = 'order_queue';
    private const PROCESSING_QUEUE_NAME = 'order_queue:processing';
    private const LOCK_TTL_SECONDS = 30;
    private const MAX_RETRY_COUNT = 3;
    private const EMPTY_QUEUE_SLEEP_SECONDS = 1;
    private const LOCK_RETRY_SLEEP_MICROSECONDS = 500000;

    protected ?int $orderId;

    public function __construct(?int $orderId = null)
    {
        $this->orderId = $orderId;
    }

    public function handle(): void
    {
        Log::info('SendOrderJob Redis worker started', [
            'bootstrap_order_id' => $this->orderId,
        ]);

        /*
         * 原本 Laravel 內建 queue 的單筆 job 處理方式保留如下，改為註解不刪除：
         *
         * Log::info("Processing order: " . $this->orderId);
         * sleep(5);
         * Log::info("Order done: " . $this->orderId);
         */

        while (true) {
            try {
                $payload = $this->dequeueForProcessing();

                if ($payload === null) {
                    Log::info('Redis order_queue is empty, SendOrderJob worker stopped.');
                    return;
                }

                $queueItem = $this->deserializeQueueItem($payload);

                if ($queueItem === null) {
                    Log::warning('Invalid Redis order_queue payload, skipped.', [
                        'payload' => $payload,
                    ]);
                    $this->completeProcessing($payload);
                    sleep(self::EMPTY_QUEUE_SLEEP_SECONDS);
                    continue;
                }

                $orderId = $queueItem['order_id'];
                $retryCount = $queueItem['retry_count'];
                $lockKey = "lock:order:{$orderId}";
                $lockToken = $this->acquireLock($lockKey);

                if ($lockToken === null) {
                    Log::warning('Order is already being processed, returning payload to order_queue.', [
                        'order_id' => $orderId,
                    ]);
                    $this->requeueProcessing($payload);
                    usleep(self::LOCK_RETRY_SLEEP_MICROSECONDS);
                    continue;
                }

                try {
                    Log::info('Processing order from Redis queue.', [
                        'order_id' => $orderId,
                        'retry_count' => $retryCount,
                    ]);

                    sleep(3);

                    $this->updateOrderStatus($orderId, Order::STATUS_CREATED);
                    $this->completeProcessing($payload);

                    Log::info('Processing order succeeded from Redis queue.', [
                        'order_id' => $orderId,
                    ]);
                } catch (Throwable $exception) {
                    $retryCount++;

                    if ($retryCount < self::MAX_RETRY_COUNT) {
                        Log::error('Retrying order from Redis queue.', [
                            'order_id' => $orderId,
                            'retry_count' => $retryCount,
                            'exception' => $exception->getMessage(),
                        ]);

                        $this->completeProcessing($payload);
                        sleep(2 ** $retryCount);
                        $this->enqueue([
                            'OrderId' => $orderId,
                            'RetryCount' => $retryCount,
                        ]);
                    } else {
                        $this->updateOrderStatus($orderId, Order::STATUS_FAILED);
                        $this->completeProcessing($payload);

                        Log::error('Order failed permanently in Redis queue worker.', [
                            'order_id' => $orderId,
                            'exception' => $exception->getMessage(),
                        ]);
                    }
                } finally {
                    $released = $this->releaseLock($lockKey, $lockToken);

                    if (! $released) {
                        Log::warning('Order lock was not released because ownership changed or it expired.', [
                            'order_id' => $orderId,
                        ]);
                    }

                    Log::info('Order lock released.', [
                        'order_id' => $orderId,
                    ]);
                }
            } catch (Throwable $exception) {
                Log::error('Unexpected error in SendOrderJob Redis worker loop.', [
                    'exception' => $exception->getMessage(),
                ]);

                sleep(3);
            }
        }
    }

    private function dequeueForProcessing(): ?string
    {
        return Redis::connection('queue_raw')->command('RPOPLPUSH', [
            self::QUEUE_NAME,
            self::PROCESSING_QUEUE_NAME,
        ]);
    }

    private function completeProcessing(string $payload): void
    {
        Redis::connection('queue_raw')->lrem(self::PROCESSING_QUEUE_NAME, 1, $payload);
    }

    private function requeueProcessing(string $payload): void
    {
        $redis = Redis::connection('queue_raw');

        $redis->lrem(self::PROCESSING_QUEUE_NAME, 1, $payload);
        $redis->rpush(self::QUEUE_NAME, $payload);
    }

    private function enqueue(array $queueItem): void
    {
        Redis::connection('queue_raw')->rpush(
            self::QUEUE_NAME,
            json_encode($queueItem, JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR)
        );
    }

    private function deserializeQueueItem(string $payload): ?array
    {
        try {
            $decoded = json_decode($payload, true, 512, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            Log::error('Failed to deserialize Redis order_queue payload.', [
                'payload' => $payload,
                'exception' => $exception->getMessage(),
            ]);

            return null;
        }

        if (! is_array($decoded)) {
            return null;
        }

        $orderId = $this->readIntValue($decoded, ['OrderId', 'orderId', 'order_id']);

        if ($orderId === null || $orderId <= 0) {
            return null;
        }

        $retryCount = $this->readIntValue($decoded, ['RetryCount', 'retryCount', 'retry_count']) ?? 0;

        return [
            'order_id' => $orderId,
            'retry_count' => $retryCount,
        ];
    }

    private function readIntValue(array $data, array $keys): ?int
    {
        foreach ($keys as $key) {
            if (! array_key_exists($key, $data)) {
                continue;
            }

            $value = $data[$key];

            if (is_int($value)) {
                return $value;
            }

            if (is_string($value) && is_numeric($value)) {
                return (int) $value;
            }
        }

        return null;
    }

    private function acquireLock(string $lockKey): ?string
    {
        $token = bin2hex(random_bytes(16));

        $acquired = Redis::connection('queue_raw')->command('SET', [
            $lockKey,
            $token,
            'EX',
            self::LOCK_TTL_SECONDS,
            'NX',
        ]);

        return $acquired ? $token : null;
    }

    private function releaseLock(string $lockKey, string $token): bool
    {
        $result = Redis::connection('queue_raw')->command('EVAL', [
            <<<'LUA'
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
end
return 0
LUA,
            1,
            $lockKey,
            $token
        ]);

        return (int) $result === 1;
    }

    private function updateOrderStatus(int $orderId, string $status): void
    {
        DB::transaction(function () use ($orderId, $status) {
            $order = Order::lockForUpdate()->find($orderId);

            if (! $order) {
                Log::warning('Order not found when updating status.', [
                    'order_id' => $orderId,
                    'status' => $status,
                ]);
                return;
            }

            if ($status === Order::STATUS_CREATED && ! $order->isPendingStatus()) {
                Log::warning('Order completion skipped because status is not pending.', [
                    'order_id' => $order->id,
                    'current_status' => $order->status,
                ]);
                return;
            }

            $order->update([
                'status' => $status,
            ]);
        });
    }
}
