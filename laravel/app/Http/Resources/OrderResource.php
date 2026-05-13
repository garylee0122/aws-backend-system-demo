<?php

namespace App\Http\Resources;

use Illuminate\Http\Request;
use Illuminate\Http\Resources\Json\JsonResource;

class OrderResource extends JsonResource
{
    /**
     * Transform the resource into an array.
     *
     * @return array<string, mixed>
     */
    public function toArray(Request $request): array
    {
        return [
            'order_id' => $this->id,
            'total_price' => $this->total_price,
            'status' => $this->status,
            'items' => OrderItemResource::collection($this->items),
            'created_at' => $this->created_at,
        ];
    }
}
