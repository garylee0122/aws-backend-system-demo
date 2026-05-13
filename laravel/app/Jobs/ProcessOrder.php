<?php

namespace App\Jobs;

use App\Models\Order;
use App\Models\Product;
use Illuminate\Bus\Queueable;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Bus\Dispatchable;
use Illuminate\Queue\InteractsWithQueue;
use Illuminate\Queue\SerializesModels;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Throwable;

class ProcessOrder implements ShouldQueue
{
    use Dispatchable, InteractsWithQueue, Queueable, SerializesModels;

    public int $tries = 3;

    public function __construct(public int $orderId)
    {
        $this->onConnection('redis');
        $this->onQueue('order_queue');
    }

    public function handle(): void
    {
        // 在同一個資料庫交易中處理訂單，確保資料的一致性和正確的鎖定行為
        DB::transaction(function () {

            // 在處理訂單之前，先鎖定訂單資料，確保在處理期間不會有其他請求修改這筆訂單的狀態
            $order = Order::with('items.product')
                ->lockForUpdate()
                ->find($this->orderId);

            if (!$order) {
                Log::warning("ProcessOrder skipped: order {$this->orderId} not found");
                return;
            }

            // 只有當訂單狀態是 pending 時才繼續處理，避免重複處理或處理已經完成/失敗的訂單
            // if ($order->status !== Order::STATUS_PENDING) {
            if (! $order->isPendingStatus()) {
                Log::warning("ProcessOrder skipped: order status is not pending", [
                    'order_id' => $order->id,
                    'status' => $order->status,
                ]);
                return;
            }

            // 模擬訂單可能處理失敗的情況，讓我們可以測試隊列任務的重試機制和失敗補償邏輯
            // Simulate occasional queue job failure for retry testing.
            if (rand(0, 1)) {
                throw new \Exception("Random failure");
            }

            sleep(3);

            // 將訂單狀態更新為 created，表示訂單已經成功處理完成
            $order->update([
                'status' => Order::STATUS_CREATED,
            ]);

            Log::info("Order processed successfully", [
                'order_id' => $order->id,
                'status' => $order->status,
            ]);
        });
    }

    public function failed(?Throwable $exception): void
    {
        DB::transaction(function () use ($exception) {
            $order = Order::with('items')->lockForUpdate()->find($this->orderId);

            if (!$order) {
                Log::error("ProcessOrder failed permanently: order {$this->orderId} not found", [
                    'order_id' => $this->orderId,
                    'exception' => $exception?->getMessage(),
                ]);
                return;
            }

            // if ($order->status === Order::STATUS_FAIL) {
            if (in_array($order->status, [Order::STATUS_FAILED, Order::LEGACY_STATUS_FAIL], true)) {
                Log::warning("ProcessOrder compensation skipped: order {$order->id} already marked as fail", [
                    'order_id' => $order->id,
                ]);
                return;
            }

            $order->update([
                'status' => Order::STATUS_FAILED,
            ]);

            Log::error("ProcessOrder failed permanently after 3 attempts. Order marked as fail", [
                'order_id' => $order->id,
                'exception' => $exception?->getMessage(),
            ]);

            $productIds = $order->items
                ->pluck('product_id')
                ->filter()
                ->unique()
                ->values();

            $products = Product::whereIn('id', $productIds)
                ->lockForUpdate()
                ->get()
                ->keyBy('id');

            foreach ($order->items as $item) {
                $product = $products->get($item->product_id);

                if (!$product) {
                    Log::warning("Inventory restore skipped: product {$item->product_id} not found for order item {$item->id}", [
                        'order_id' => $order->id,
                        'order_item_id' => $item->id,
                        'product_id' => $item->product_id,
                    ]);
                    continue;
                }

                $product->increment('stock', $item->quantity);

                Log::info("Inventory restored for failed order", [
                    'order_id' => $order->id,
                    'order_item_id' => $item->id,
                    'product_id' => $product->id,
                    'restored_quantity' => $item->quantity,
                ]);
            }
        });
    }
}
