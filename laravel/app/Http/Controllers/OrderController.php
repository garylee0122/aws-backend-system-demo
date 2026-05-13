<?php

namespace App\Http\Controllers;

use App\Helpers\ApiResponse;
use App\Http\Requests\StoreOrderRequest;
use App\Http\Resources\OrderResource;
use App\Jobs\ProcessOrder;
use App\Models\Order;
use App\Models\Product;
use Illuminate\Cache\RedisStore;
use Illuminate\Cache\TaggedCache;
use Illuminate\Http\Request;
use Illuminate\Redis\Connections\PredisConnection;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\Facades\Redis;
use Illuminate\Validation\ValidationException;

class OrderController extends Controller
{
    // 訂單相關快取的 TTL，統一設定為 5 分鐘（300 秒）
    private const ORDER_CACHE_TTL_SECONDS = 300;

    public function store(StoreOrderRequest $request)
    {
        $user = $request->user();
        $items = $request->items;

        $order = DB::transaction(function () use ($user, $items) {
            $totalPrice = 0;
            $orderItemsData = [];

            // 先整理出本次訂單涉及的商品 ID，避免重複查詢
            $productIds = collect($items)->pluck('product_id')->unique()->values();

            // 鎖定商品資料列，避免多人同時下單造成超賣
            $products = Product::whereIn('id', $productIds)
                ->lockForUpdate()
                ->get()
                ->keyBy('id');

            foreach ($items as $item) {
                $product = $products->get($item['product_id']);

                // 商品不存在時中止交易
                if (! $product) {
                    Log::warning("Product {$item['product_id']} does not exist.");
                    throw ValidationException::withMessages([
                        'items' => ["Product {$item['product_id']} does not exist."],
                    ]);
                }

                // 庫存不足時中止交易
                if ($product->stock < $item['quantity']) {
                    Log::warning("Product {$product->id} stock is insufficient. Requested: {$item['quantity']}, Available: {$product->stock}");
                    throw ValidationException::withMessages([
                        'items' => ["Product {$product->id} stock is insufficient."],
                    ]);
                }

                // 直接在資料庫做遞減，避免併發更新問題
                $product->decrement('stock', $item['quantity']);

                $subtotal = $product->price * $item['quantity'];
                $totalPrice += $subtotal;

                $orderItemsData[] = [
                    'product_id' => $product->id,
                    'price' => $product->price,
                    'quantity' => $item['quantity'],
                ];
            }

            $order = $user->orders()->create([
                'total_price' => $totalPrice,
                'status' => Order::STATUS_PENDING,
            ]);

            foreach ($orderItemsData as $data) {
                $order->items()->create($data);
            }

            return $order->load('items.product');
        });

        // 商品庫存變動後，清除商品相關快取
        Cache::tags(['products'])->flush();

        // 訂單資料有新增時，清除該使用者的訂單列表與單筆訂單快取
        Cache::tags($this->getUserOrderCacheTags($user->id))->flush();

        /* Region: 訂單建立後推送 Queue */

        // 若改回 Laravel Queue，可啟用這行
        // ProcessOrder::dispatch($order->id);

        // 目前改為直接推送到 Redis list，提供 ASP.NET Worker Service 消費
        Redis::connection('queue_raw')->rpush('order_queue', json_encode([
            'OrderId' => $order->id,
            'RetryCount' => 0,
        ], JSON_UNESCAPED_UNICODE));

        /* End Region */

        return ApiResponse::success(
            new OrderResource($order),
            'Order created successfully, processing in background',
            201
        );
    }

    public function index(Request $request)
    {
        $user = $request->user();
        $page = $request->integer('page', 1);

        // 依照使用者與頁碼建立快取 key，避免不同頁面互相覆蓋
        $cacheKey = 'user:' . $user->id . ':orders:page:' . $page;

        $orders = Cache::tags($this->getUserOrderCacheTags($user->id))->remember($cacheKey, self::ORDER_CACHE_TTL_SECONDS, function () use ($user) {
            return $user->orders()
                ->with('items.product')
                ->latest()
                ->paginate(5);
        });

        return ApiResponse::success(
            OrderResource::collection($orders),
            'get all orders by ' . $user->name . ' successfully'
        );
    }

    public function show(Request $request, $id)
    {
        $user = $request->user();
        $cacheKey = 'user:' . $user->id . ':order:' . $id;
        $loadedFromCache = true;

        // 單筆訂單也掛上同一組 tag，建立新訂單時就能一起清除
        $taggedCache = Cache::tags($this->getUserOrderCacheTags($user->id));

        $order = $taggedCache->remember($cacheKey, self::ORDER_CACHE_TTL_SECONDS, function () use ($request, $id, &$loadedFromCache) {
            $loadedFromCache = false;

            return $request->user()
                ->orders()
                ->with('items.product')
                ->findOrFail($id);
        });

        // 方便在 local 環境對照 Redis Insight，確認實際 key 與 TTL
        $cacheDebug = $this->getRedisCacheDebugInfo($taggedCache, $cacheKey);

        Log::info('Order show data source', [
            'order_id' => $id,
            'user_id' => $user->id,
            'source' => $loadedFromCache ? 'redis cache' : 'db',
            'cache_key' => $cacheKey,
            'cache_tags' => $this->getUserOrderCacheTags($user->id),
            'redis_connection' => $cacheDebug['connection'],
            'redis_db' => $cacheDebug['database'],
            'redis_key' => $cacheDebug['redis_key'],
            'redis_ttl' => $cacheDebug['ttl'],
        ]);

        return ApiResponse::success(
            new OrderResource($order),
            'get order (' . $order->id . ') by ' . $request->user()->name . ' successfully'
        );
    }

    // 訂單快取統一使用兩個 tag：
    // 'orders' 代表所有訂單快取
    // 'user:{userId}:orders' 代表指定使用者的訂單快取
    private function getUserOrderCacheTags(int $userId): array
    {
        return [
            'orders',
            'user:' . $userId . ':orders',
        ];
    }

    private function getRedisCacheDebugInfo($cacheRepository, string $cacheKey): array
    {
        $store = $cacheRepository->getStore();

        // 只有 Redis store 才能進一步解析實際 key 與 TTL
        if (! $store instanceof RedisStore) {
            return [
                'connection' => null,
                'database' => null,
                'redis_key' => null,
                'ttl' => null,
            ];
        }

        $connectionName = config('cache.stores.redis.connection', 'cache');
        $database = config("database.redis.{$connectionName}.database");
        $redisConnection = $store->connection();

        // 使用 tag 時，Laravel 會先把原始 key 轉成帶 namespace 的 key
        $resolvedCacheKey = $cacheRepository instanceof TaggedCache
            ? $cacheRepository->taggedItemKey($cacheKey)
            : $cacheKey;
        $redisKey = $this->getRedisConnectionPrefix($redisConnection).$store->getPrefix().$resolvedCacheKey;

        return [
            'connection' => $connectionName,
            'database' => $database,
            'redis_key' => $redisKey,
            'ttl' => $redisConnection->ttl($redisKey),
        ];
    }

    private function getRedisConnectionPrefix($redisConnection): string
    {
        if ($redisConnection instanceof PredisConnection) {
            return (string) ($redisConnection->client()->getOptions()->prefix ?? '');
        }

        return '';
    }
}
