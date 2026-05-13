using DemoAPI.Data;
using DemoAPI.Infrastructure.Services;
using DemoAPI.Infrastructure.Workers;
using DemoAPI.Infrastructure.Queues;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IO;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var redisConfig = builder.Configuration.GetSection("Redis");
var redisConnectionString = builder.Environment.IsDevelopment()
    ? redisConfig["ConnectionStringByLocalhost"] ?? redisConfig["ConnectionString"]
    : redisConfig["ConnectionString"];
var enableOrderWorkerValue = builder.Configuration["EnableOrderWorker"]
    ?? Environment.GetEnvironmentVariable("ENABLE_ORDER_WORKER");
var enableOrderWorker = !string.Equals(enableOrderWorkerValue, "false", StringComparison.OrdinalIgnoreCase);
var workerName =
    Environment.GetEnvironmentVariable("WORKER_NAME")
    ?? Environment.GetEnvironmentVariable("HOSTNAME")
    ?? Environment.MachineName;
var safeWorkerName = string.Join(
    "_",
    workerName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
var logOutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{WorkerName}/pid:{ProcessId}] {SourceContext} {Message:lj}{NewLine}{Exception}";

if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("Missing Redis:ConnectionString configuration.");
}

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console(outputTemplate: logOutputTemplate)
        .WriteTo.File(
            path: Path.Combine("logs", $"{safeWorkerName}-log-.txt"),
            rollingInterval: RollingInterval.Day,
            shared: true,
            outputTemplate: logOutputTemplate)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("WorkerName", workerName)
        .Enrich.WithProperty("ProcessId", Environment.ProcessId));

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<CacheInvalidationService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<OrderQueue>(); // 使用 memory queue
builder.Services.AddSingleton<RedisQueueService>(); // 使用 Redis queue
builder.Services.AddSingleton<RedisLockService>(); // 使用 Redis lock
// builder.Services.AddHostedService<OrderWorker>(); // 保留原本一律啟用 Worker 的寫法
if (enableOrderWorker)
{
    builder.Services.AddHostedService<OrderWorker>();
}

// for global model validation error response
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        return new BadRequestObjectResult(new
        {
            status = "error",
            errors = context.ModelState
        });
    };
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256 bits) for HS256.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
});

// Redis Queue
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 3;
    return ConnectionMultiplexer.Connect(options);
});

// sqlite
//builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=products.db"));

// mysql
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// mark for docker, in production we will use nginx to handle https
//app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();

try
{
    Log.Information("Starting DemoAPI");
    Log.Information("Order worker enabled: {EnableOrderWorker}", enableOrderWorker);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "DemoAPI terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
