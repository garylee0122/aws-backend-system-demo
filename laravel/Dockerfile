FROM php:8.2-cli

# 1. 優化：將系統套件與 PHP 擴充合併在同一個步驟，減少不必要的 Docker Layer 殘留
# 2. 優化：改用官方推薦的互助腳本「install-php-extensions」，它安裝 Redis 速度極快且極省記憶體！
ADD https://github.com/mlocati/docker-php-extensions/releases/latest/download/install-php-extensions /usr/local/bin/

RUN chmod +x /usr/local/bin/install-php-extensions && \
    apt-get update && apt-get install -y \
    git \
    curl \
    unzip \
    zip \
    && install-php-extensions pdo pdo_mysql zip redis \
    && rm -rf /var/lib/apt/lists/*

# 安裝 Composer
COPY --from=composer:latest /usr/bin/composer /usr/bin/composer

WORKDIR /var/www

# 利用快取優化
COPY composer.json composer.lock ./

RUN composer install \
    --no-dev \
    --optimize-autoloader \
    --no-interaction \
    --prefer-dist

# 複製專案
COPY . .

# 權限與快取（合併在一起減少層數）
RUN chown -R www-data:www-data /var/www \
    && chmod -R 775 storage bootstrap/cache \
    && php artisan config:cache \
    && php artisan route:cache \
    && php artisan view:cache

EXPOSE 8000

CMD ["php", "artisan", "serve", "--host=0.0.0.0", "--port=8000"]