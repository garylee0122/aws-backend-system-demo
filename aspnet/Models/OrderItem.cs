using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace DemoAPI.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public int Price { get; set; }
        public int Quantity { get; set; }
    }
}