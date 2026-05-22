FROM php:8.2-cli

# 1. 優化：將系統套件與 PHP 擴充合併在同一個步驟，減少不必要的 Docker Layer 殘留
# 2. 優化：改用官方推薦的互助腳本「install-php-extensions」，它安裝 Redis 速度極快且極省記憶體！
ADD https://github.com/mlocati/docker-php-extension-installer/releases/latest/download/install-php-extensions /usr/local/bin/

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

# 工作目錄
WORKDIR /var/www

# === 【調整重點：修正順序】 ===

# 1. 先把完整專案檔案（包含 artisan）複製進去
COPY . .

# 2. 緊接著執行 composer install（這時 artisan 存在，能順利跑完 post-autoload-dump，並建立 vendor）
RUN composer install \
    --no-dev \
    --optimize-autoloader \
    --no-interaction \
    --prefer-dist

# 3. 有了 vendor 之後，最後才調權限與建立 Laravel 快取（這樣就不會噴 vendor/autoload.php 找不到的錯了）
RUN chown -R www-data:www-data /var/www \
    && chmod -R 775 storage bootstrap/cache \
    && php artisan config:cache \
    && php artisan route:cache \
    && php artisan view:cache

EXPOSE 8000

CMD ["php", "artisan", "serve", "--host=0.0.0.0", "--port=8000"]