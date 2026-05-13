using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DemoAPI.DTOs
{
    public class OrderQueueItem
    {
        public int OrderId { get; set; }
        public int RetryCount { get; set; } = 0;
    }
}