# AWS Backend System Demo

A distributed backend system built with Laravel, ASP.NET Core, Redis, Docker, Nginx, and AWS EC2.

本專案主要展示一套接近真實業界的 Backend System Architecture，包含 API Server、Queue Worker、Cache、Docker Container、Nginx Reverse Proxy 與 AWS 雲端部署。

This project demonstrates a production-style backend architecture including:

- RESTful API  
  建立符合 RESTful 規範的 API 架構

- Queue-based asynchronous processing  
  使用 Queue 實作非同步背景工作處理

- Redis Cache / Queue / Distributed Lock  
  使用 Redis 實作 Cache、Queue 與分散式鎖

- Retry mechanism  
  當 Queue Job 處理失敗時，自動 Retry 避免資料遺失

- Docker containerization  
  使用 Docker 封裝整個系統環境

- Nginx reverse proxy  
  使用 Nginx 作為 Reverse Proxy 與 API 入口

- AWS EC2 deployment  
  部署於 AWS EC2 Ubuntu Server

---

# Architecture

```text
                Client / Browser
                        ↓
                     Nginx
               ┌────────┴────────┐
               ↓                 ↓
        Laravel API        ASP.NET Worker/API
               ↓                 ↓
               └────────┬────────┘
                        ↓
                      Redis
          (Queue / Cache / Distributed Lock)
                        ↓
                      MySQL
```

系統主要透過 Nginx 作為入口，將請求轉發到 Laravel API 與 ASP.NET Worker/API，並透過 Redis 處理 Queue、Cache 與 Lock 機制，最後由 MySQL 負責正式資料儲存。

---

# Tech Stack

## Backend

- PHP 8.x
- Laravel
- C# / ASP.NET Core

主要使用 Laravel 與 ASP.NET Core 建立 API 與 Background Worker。

---

## Infrastructure

- Docker
- Docker Compose
- Nginx
- AWS EC2 (Ubuntu)

使用 Docker Compose 管理整套服務，並部署於 AWS EC2 Ubuntu Server。

---

## Database

- MySQL / MariaDB
- Redis

MySQL 負責正式資料儲存，Redis 負責 Queue、Cache 與 Distributed Lock。

---

# Features

## API System

- RESTful API  
  建立標準 RESTful API 架構

- Authentication  
  API 身份驗證機制

- CRUD Operations  
  提供完整新增、查詢、修改、刪除功能

- Pagination  
  API 分頁查詢功能

- Resource Wrapping  
  統一 API Response Resource 格式

- Unified API Response  
  統一 API 回傳格式，方便前後端整合

---

# Order System

- Order creation  
  建立訂單功能

- Order status management  
  訂單狀態管理

- Asynchronous order processing  
  使用 Queue 進行非同步訂單處理

- Queue-based architecture  
  使用 Queue-based Architecture 提升系統可擴展性

---

# Redis Features

## Queue

- Redis Queue integration  
  使用 Redis 作為 Queue Broker

- ASP.NET Background Worker  
  ASP.NET Background Service 負責消費 Queue

- Asynchronous job processing  
  背景非同步處理工作流程

---

## Retry Mechanism

- Automatic retry for failed jobs  
  Queue Job 失敗時自動 Retry

- Prevents temporary failures from losing tasks  
  避免暫時性錯誤導致資料遺失

---

## Distributed Lock

- Redis Lock  
  使用 Redis Distributed Lock

- Prevent duplicate order processing  
  避免重複處理同一筆訂單

- High concurrency protection  
  保護高併發情境下的資料一致性

---

## Cache

- Cache Aside Pattern  
  使用 Cache Aside Pattern

- Redis caching for order queries  
  使用 Redis Cache 加速訂單查詢

- Cache invalidation after update  
  更新資料後自動清除 Cache

---

# Order Flow

```text
1. Client creates order
2. Laravel API stores order into MySQL
3. Laravel pushes order job into Redis Queue
4. ASP.NET Worker consumes queue
5. Worker processes order
6. Worker updates order status
7. Redis cache invalidation triggered
```

訂單建立後，Laravel API 會先寫入 MySQL，再將 Job Push 到 Redis Queue，由 ASP.NET Worker 進行背景處理，最後更新訂單狀態並清除 Cache。

---

# Docker Architecture

```text
Docker Compose
 ├── nginx
 ├── laravel
 ├── aspnet
 ├── redis
 └── mysql
```

使用 Docker Compose 管理所有 Container，讓整個系統能快速部署與啟動。

---

# Nginx Reverse Proxy

Nginx acts as the single entry point for the system.

```text
http://server-ip
    ├── ASP.NET
    └── /laravel → Laravel
```

Nginx 負責統一入口與 Reverse Proxy Routing。

---

# AWS Deployment

This project is deployed on:

- AWS EC2
- Ubuntu Server
- Docker Compose
- Nginx Reverse Proxy

系統部署於 AWS EC2 Ubuntu Server，並透過 Docker Compose 管理整套服務。

---

# Run Locally

## Clone Project

```bash
git clone https://github.com/YOUR_GITHUB/aws-backend-system-demo.git
```

---

## Start Containers

```bash
docker compose up -d --build
```

啟動所有 Docker Containers。

---

# Access Services

| Service | URL |
|---|---|
| Laravel | http://laravel.localhost/ |
| ASP.NET | http://localhost |
| phpMyAdmin | http://localhost:8080 |
| Redis Commander | http://localhost:8081 |

---

# Environment Variables

## Laravel -> .env

```env
APP_ENV=production
APP_DEBUG=false

DB_HOST=mysql
REDIS_HOST=redis
```

Docker Compose 中可直接透過 service name 作為 Host Name。

---

# Future Improvements

- HTTPS / SSL  
  加入 HTTPS 安全連線

- GitHub Actions CI/CD  
  建立自動化部署流程

- AWS RDS  
  使用 AWS RDS 管理資料庫

- Kubernetes  
  Container Orchestration

- Monitoring (Prometheus / Grafana)  
  系統監控與 Metrics

- Load Balancer  
  負載平衡

- Auto Scaling  
  自動擴展機制

---

# Learning Goals

This project was created for learning:

- Backend system architecture
- Distributed systems basics
- Docker deployment
- AWS infrastructure
- Queue / Cache design
- Production environment setup

本專案主要用於學習 Backend System Design、Docker Deployment、AWS Infrastructure 與 Distributed System 基礎架構。

---

# Author

Gary Lee