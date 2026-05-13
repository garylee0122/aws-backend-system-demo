<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Order extends Model
{
    const STATUS_PENDING = 'Pending';
    const STATUS_CREATED = 'Created';
    const STATUS_PAID = 'Paid';
    const STATUS_FAILED = 'Failed';
    const STATUS_CANCELLED = 'Cancelled';

    const LEGACY_STATUS_PENDING = 'pending';
    const LEGACY_STATUS_COMPLETED = 'completed';
    const LEGACY_STATUS_PAID = 'paid';
    const LEGACY_STATUS_FAIL = 'fail';
    const LEGACY_STATUS_CANCELLED = 'cancelled';

    protected $fillable = [
        'user_id',
        'total_price',
        'status'
    ];

    public function user()
    {
        return $this->belongsTo(User::class);
    }

    public function items()
    {
        return $this->hasMany(OrderItem::class);
    }

    public function isPendingStatus(): bool
    {
        return in_array($this->status, [
            self::STATUS_PENDING,
            self::LEGACY_STATUS_PENDING,
        ], true);
    }
}
