<?php

namespace App\Services;

use App\Models\Product;
use App\Models\User;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Log;

class ProductService
{
    private const CACHE_TTL_SECONDS = 60;

    public function getProducts(User $user, $request)
    {
        $keyword = $request->keyword ?? 'all';
        $page = $request->integer('page', 1);
        $cacheKey = "products_user_{$user->id}_{$keyword}_page_{$page}";
        $cache = Cache::tags(['products']);
        $source = $cache->has($cacheKey) ? 'cache' : 'db';

        // 記錄開始時間
        $startTime = microtime(true);

        $result = $cache->remember($cacheKey, self::CACHE_TTL_SECONDS, function () use ($request, $user) {

            $query = Product::query()
                ->where('user_id', $user->id)
                ->when($request->filled('keyword'), function ($query) use ($request) {
                    $query->where('name', 'like', '%' . $request->keyword . '%');
                });

            return $query->paginate(10);
        });

        // 計算從開始到獲取結果的時間（毫秒）
        $fetchDurationMs = round((microtime(true) - $startTime) * 1000, 2);

        Log::info('Products fetched', [
            'cache_key' => $cacheKey,
            'source' => $source,
            'fetch_duration_ms' => $fetchDurationMs,
        ]);

        return [
            'products' => $result,
            'meta' => [
                'source' => $source,
                'fetch_duration_ms' => $fetchDurationMs,
            ],
        ];
    }

    public function createProduct(User $user, array $data)
    {
        return $user->products()->create($data);
    }

    public function updateProduct(User $user, $id, array $data)
    {
        $product = $user->products()->findOrFail($id);
        $product->update($data);

        return $product->refresh();
    }

    public function deleteProduct(User $user, $id)
    {
        $product = $user->products()->findOrFail($id);
        $product->delete();
    }

    public function getProduct(User $user, $id)
    {
        return $user->products()->findOrFail($id);
    }
}
