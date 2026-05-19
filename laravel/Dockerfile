FROM php:8.2-fpm

# 安裝系統套件
RUN apt-get update && apt-get install -y \
    git \
    curl \
    unzip \
    zip \
    libzip-dev \
    libonig-dev \
    libxml2-dev

# PHP extensions
RUN docker-php-ext-install \
    pdo \
    pdo_mysql \
    zip

# Redis extension
RUN pecl install redis \
    && docker-php-ext-enable redis

# Composer
COPY --from=composer:latest /usr/bin/composer /usr/bin/composer

WORKDIR /var/www

# 先複製 composer 檔🔥
COPY composer.json composer.lock ./

# Composer install🔥
RUN composer install --no-dev --optimize-autoloader --no-scripts

# 再複製其他檔案🔥
COPY . .

# Laravel cache
RUN php artisan config:cache || true
RUN php artisan route:cache || true

# 權限
RUN chmod -R 775 storage bootstrap/cache

EXPOSE 8000

CMD php artisan serve --host=0.0.0.0 --port=8000