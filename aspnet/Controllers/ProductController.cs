using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using DemoAPI.Infrastructure.Services;
using DemoAPI.DTOs;
using DemoAPI.Helpers;

namespace DemoAPI.Controllers
{
    [ApiController]
    [Route("api/products")]
    public class ProductController : ControllerBase
    {
        private readonly ProductService _service;

        public ProductController(ProductService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? keyword, [FromQuery] int page = 1)
        {
            var userId = getClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var data = await _service.GetAll(keyword, page, userId);

            if (data == null)
            {
                return NotFound(new ApiResponse<string>
                {
                    Status = "error",
                    Message = "Product not found"
                });
            }

            return Ok(new ApiResponse<PagedResultDto<ProductResponseDto>>
            {
                Data = data
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var userId = getClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var data = await _service.GetById(id, userId);

            if (data == null)
            {
                return NotFound(new ApiResponse<string>
                {
                    Status = "error",
                    Message = "Product not found"
                });
            }

            return Ok(new ApiResponse<ProductResponseDto>
            {
                Data = data
            });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create(CreateProductDto dto)
        {
            var userId = getClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            dto.UserId = userId;
            var data = await _service.Create(dto);

            return Ok(new ApiResponse<ProductResponseDto>
            {
                Data = data
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, UpdateProductDto dto)
        {
            var userId = getClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            dto.UserId = userId;
            var data = await _service.Update(id, dto);

            return Ok(new ApiResponse<ProductResponseDto>
            {
                Data = data
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = getClaimUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await _service.Delete(id, userId);

            if (!result)
            {
                return NotFound(new ApiResponse<string>
                {
                    Status = "error",
                    Message = "Product not found"
                });
            }

            return Ok(new ApiResponse<string>
            {
                Status = "success",
                Message = "Product deleted successfully"
            });
        }

        private int? getClaimUserId()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return null;
            }

            return int.Parse(userIdClaim.Value ?? "0");
        }
    }
}
