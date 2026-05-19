FROM php:8.2-fpm

# 安裝系統套件
RUN apt-get update && apt-get install -y \
    git \
    curl \
    unzip \
    zip \
    libzip-dev \
    libonig-dev \
    libxml2-dev \
    && rm -rf /var/lib/apt/lists/*

# 安裝 PHP extensions
RUN docker-php-ext-install \
    pdo \
    pdo_mysql \
    zip

# 安裝 Redis extension
RUN pecl install redis \
    && docker-php-ext-enable redis

# 安裝 Composer
COPY --from=composer:latest /usr/bin/composer /usr/bin/composer

# 設定工作目錄
WORKDIR /var/www

# 先複製 composer 檔案（build cache optimization🔥）
COPY composer.json composer.lock ./

# 安裝 PHP 套件
RUN composer install \
    --no-dev \
    --optimize-autoloader \
    --no-interaction \
    --prefer-dist

# 再複製 Laravel 專案
COPY . .

# Laravel optimize
RUN php artisan config:cache
RUN php artisan route:cache
RUN php artisan view:cache

# 權限
RUN chown -R www-data:www-data /var/www \
    && chmod -R 775 storage bootstrap/cache

EXPOSE 9000

CMD ["php-fpm"]