<?php
// 必須註解掉 api.php 中的路由，才能在 web.php 中使用相同的路由定義，並且加上前綴 'laravel-api' 來區分。
// 否則再 docker build 的時候會因為路由重複而報錯。
/*
use Illuminate\Support\Facades\Route;
use App\Http\Controllers\AuthController;
use App\Http\Controllers\ProductController;
use App\Http\Controllers\OrderController;
use Illuminate\Http\Request;

Route::post('/register', [AuthController::class, 'register']);
Route::post('/login', [AuthController::class, 'login']);

Route::middleware('auth:sanctum')->get('/me', function (Request $request) {
    return $request->user();
});

Route::middleware('auth:sanctum')->group(function () {
    Route::apiResource('products', ProductController::class);
});

Route::middleware('auth:sanctum')->group(function () {
    Route::post('orders', [OrderController::class, 'store']);
    Route::get('orders', [OrderController::class, 'index']);
    Route::get('orders/{id}', [OrderController::class, 'show']);
});

use App\Jobs\SendOrderJob;
Route::get('/test-queue', function () {
    SendOrderJob::dispatch(123);
    return "Job dispatched!";
});
*/
