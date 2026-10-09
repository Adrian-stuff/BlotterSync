using BlotterSync.DTOs;
using BlotterSync.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BlotterSync.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly BlotterSyncContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(BlotterSyncContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginDTO loginDto)
        {
            var user = await _context.Officers.FirstOrDefaultAsync(u => u.Username == loginDto.Username);

            if (user == null || user.PasswordHash != loginDto.Password)
            {
                return Unauthorized("Invalid username or password.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.OfficerId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(8),
                signingCredentials: creds
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                role = user.Role,
                officerId = user.OfficerId
            });
        }
        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterDTO registerDto)
        {
            // 1. Check if the username is already taken
            if (await _context.Officers.AnyAsync(u => u.Username == registerDto.Username))
            {
                return BadRequest("Username already exists.");
            }

            // 2. Map the DTO to the database model
            var newOfficer = new Officer
            {
                BadgeNumber = registerDto.BadgeNumber,
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                Username = registerDto.Username,
                PasswordHash = registerDto.Password, // In a real app, hash this!
                Role = "Officer", // Force new accounts to be standard officers
                ActiveStatus = true
            };

            // 3. Save to SQL Server
            _context.Officers.Add(newOfficer);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful. You can now log in." });
        }
        [AllowAnonymous]
        [HttpGet("Public")]
        public async Task<ActionResult<IEnumerable<object>>> GetPublicBlotterData()
        {
            // Returns ONLY safe data for the Kiosk map and charts
            var publicRecords = await _context.BlotterRecords
                .Select(b => new {
                    b.IncidentDate,
                    b.Location,
                    b.Status
                }).ToListAsync();

            return Ok(publicRecords);
        }
    }
}