using DemoAPI.Enums;
using Microsoft.EntityFrameworkCore;
using DemoAPI.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DemoAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }

        public DbSet<User> Users { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var orderStatusConverter = new ValueConverter<OrderStatus, string>(
                status => status.ToString(),
                value => Enum.Parse<OrderStatus>(value, true));

            // 為了配合資料庫的命名慣例，將實體類別與資料表名稱對應起來，並設定欄位名稱，以利跟 Laravel 的資料表結構一致
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders");

                entity.Property(order => order.UserId)
                    .HasColumnName("user_id");

                entity.Property(order => order.TotalPrice)
                    .HasColumnName("total_price");

                entity.Property(order => order.Status)
                    .HasColumnName("status")
                    .HasConversion(orderStatusConverter);
            });

            // 為了配合資料庫的命名慣例，將實體類別與資料表名稱對應起來，並設定欄位名稱，以利跟 Laravel 的資料表結構一致
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("order_items");

                entity.Property(orderItem => orderItem.OrderId)
                    .HasColumnName("order_id");

                entity.Property(orderItem => orderItem.ProductId)
                    .HasColumnName("product_id");
            });

            // 為了配合資料庫的命名慣例，將實體類別與資料表名稱對應起來，並設定欄位名稱，以利跟 Laravel 的資料表結構一致
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("products");

                entity.Property(product => product.UserId)
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<User>()
                .ToTable("users");

            base.OnModelCreating(modelBuilder);
        }
    }
}
