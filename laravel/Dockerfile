FROM php:8.2-fpm

# 安裝套件
RUN apt-get update && apt-get install -y \
    git \
    unzip \
    libzip-dev \
    zip \
    curl

# 安裝 PHP extension
RUN docker-php-ext-install pdo pdo_mysql zip

# 安裝 Composer
COPY --from=composer:latest /usr/bin/composer /usr/bin/composer

# 設定工作目錄
WORKDIR /var/www

# 先複製 composer（加快 build🔥）
# COPY composer.json composer.lock ./

# RUN composer install --no-scripts --no-autoloader

# 再複製全部
COPY . .

# RUN composer dump-autoload

# Laravel 權限
# RUN chmod -R 775 storage bootstrap/cache

RUN composer install

CMD ["php-fpm"]