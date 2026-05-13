using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace DemoAPI.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public int TotalPrice { get; set; }
        public string Status { get; set; } = "";
        public List<OrderItemDto> Items { get; set; } = new();
    }
}