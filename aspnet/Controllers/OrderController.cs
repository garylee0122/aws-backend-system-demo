using DemoAPI.DTOs;
using DemoAPI.Helpers;
using DemoAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DemoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly OrderService _service;

        public OrderController(OrderService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateOrderDto dto)
        {
            var userId = GetClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await _service.Create(dto, userId.Value);
            if (!result.IsSuccess)
            {
                return StatusCode(result.StatusCode, new ApiResponse<string>
                {
                    Status = "error",
                    Message = result.ErrorMessage
                });
            }

            return Ok(new ApiResponse<OrderDto>
            {
                Message = "Order created (processing...)",
                Data = result.Data
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetMyOrders()
        {
            var userId = GetClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var orders = await _service.GetMyOrders(userId.Value);

            return Ok(new ApiResponse<List<OrderDto>>
            {
                Data = orders
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            var userId = GetClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var order = await _service.GetById(id, userId.Value);
            if (order == null)
            {
                return NotFound(new ApiResponse<string>
                {
                    Status = "error",
                    Message = "Order not found"
                });
            }

            return Ok(new ApiResponse<OrderDto>
            {
                Data = order
            });
        }

        private int? GetClaimUserId()
        {
            var userIdClaim = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return null;
            }

            return int.Parse(userIdClaim.Value ?? "0");
        }
    }
}
