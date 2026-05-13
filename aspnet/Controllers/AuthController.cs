using System;
using System.Linq;
using System.Text;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DemoAPI.Infrastructure.Services;
using DemoAPI.DTOs;
using DemoAPI.Helpers;
using DemoAPI.Data;
using DemoAPI.Models;
using Microsoft.AspNetCore.Identity;

namespace DemoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("register")]
        public IActionResult Register(RegisterDto user)
        {
            if (_context.Users.Any(u => u.Email == user.Email))
            {
                return BadRequest("Email already exists");
            }

            var hasher = new PasswordHasher<RegisterDto>();
            user.Password = hasher.HashPassword(user, user.Password);

            var newuser = new User
            {
                Name = user.Name,
                Email = user.Email,
                Password = user.Password
            };

            _context.Users.Add(newuser);
            _context.SaveChanges();

            return Ok(new
            {
                message = "User registered successfully",
                user = new { newuser.Id, newuser.Name, newuser.Email }
            });
        }

        [HttpPost("login")]
        public IActionResult Login(LoginDto loginUser)
        {
            var user = _context.Users
                .FirstOrDefault(u => u.Email == loginUser.Email);

            if (user == null)
            {
                return Unauthorized("Invalid credentials");
            }

            var hasher = new PasswordHasher<User>();
            var isMatch = hasher.VerifyHashedPassword(user, user.Password, loginUser.Password);
            if (isMatch == PasswordVerificationResult.Failed)
            {
                return Unauthorized("Invalid credentials");
            }

            /* for Generate JWT token START */ 
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
            };

            var jwtKey = _config["Jwt:Key"];
            if (jwtKey == null)
            {
                throw new InvalidOperationException("JWT Key is not configured.");
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            );

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds
            );
            /* for Generate JWT token END */

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token)
            });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(User.Claims.Select(c => new { c.Type, c.Value }));
        }
    }

}
