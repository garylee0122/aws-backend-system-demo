<?php

use Illuminate\Support\Facades\Route;
use App\Http\Controllers\AuthController;
use App\Http\Controllers\ProductController;
use App\Http\Controllers\OrderController;
use Illuminate\Http\Request;
use App\Jobs\SendOrderJob;

/*
|--------------------------------------------------------------------------
| Laravel Web 首頁
|--------------------------------------------------------------------------
*/

Route::get('/', function () {
    return view('welcome');
});

/*
|--------------------------------------------------------------------------
| Laravel API
| 為了滿足 nginx 的 location 配置，我們將 API 路由放在 web.php 中，並使用前綴 'laravel-api' 來區分。
|--------------------------------------------------------------------------
*/

Route::prefix('laravel-api')->group(function () {

    Route::get('/test', function () {
        return response()->json([
            'message' => 'Laravel API OK'
        ]);
    });

    Route::post('/register', [AuthController::class, 'register']);

    Route::post('/login', [AuthController::class, 'login']);

    Route::middleware('auth:sanctum')->get('/me', function (Request $request) {
        return $request->user();
    });

    Route::middleware('auth:sanctum')->group(function () {

        Route::apiResource('products', ProductController::class);

        Route::post('orders', [OrderController::class, 'store']);

        Route::get('orders', [OrderController::class, 'index']);

        Route::get('orders/{id}', [OrderController::class, 'show']);
    });

    Route::get('/test-queue', function () {

        SendOrderJob::dispatch(123);

        return "Job dispatched!";
    });

});