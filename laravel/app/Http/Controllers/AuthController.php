<?php

namespace App\Http\Controllers;

use App\Helpers\ApiResponse;
use App\Models\User;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;

class AuthController extends Controller
{
    public function register(Request $request)
    {
        $data = $request->validate([
            'name' => 'required|string|max:255',
            'email' => 'required|string|email|max:255|unique:users',
            'password' => 'required|string|min:6',
        ]);

        $user = User::create([
            'name' => $data['name'],
            'email' => $data['email'],
            'password' => $data['password'],
        ]);

        return ApiResponse::success($user, 'User registered successfully');
    }

    public function login(Request $request)
    {
        $data = $request->validate([
            'email' => 'required|email',
            'password' => 'required',
        ]);

        if (! Auth::attempt($data)) {
            return ApiResponse::error('Invalid credentials', 401);
        }

        /** @var User|null $user */
        $user = Auth::user();

        if (! $user instanceof User) {
            return ApiResponse::error('Authenticated user not found', 401);
        }

        $token = $user->createToken('api-token')->plainTextToken;

        return ApiResponse::success([
            'token' => $token,
        ], 'Login successful');
    }
}
