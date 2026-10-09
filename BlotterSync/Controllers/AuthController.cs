using BlotterSync.DTOs;
using BlotterSync.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BlotterSync.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly BlotterSyncContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasher<Officer> _passwordHasher;

        public AuthController(BlotterSyncContext context, IConfiguration configuration, IPasswordHasher<Officer> passwordHasher)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginDTO loginDto)
        {
            var user = await _context.Officers.FirstOrDefaultAsync(u => u.Username == loginDto.Username);

            if (user == null || user.ActiveStatus == false || !await VerifyPasswordAsync(user, loginDto.Password))
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
                expires: DateTime.UtcNow.AddHours(8),
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
            // 1. Check if the username or badge number is already taken
            if (await _context.Officers.AnyAsync(u => u.Username == registerDto.Username))
            {
                return BadRequest("Username already exists.");
            }
            if (await _context.Officers.AnyAsync(u => u.BadgeNumber == registerDto.BadgeNumber))
            {
                return BadRequest("Badge number is already registered.");
            }

            // 2. Map the DTO to the database model
            var newOfficer = new Officer
            {
                BadgeNumber = registerDto.BadgeNumber,
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                Username = registerDto.Username,
                Role = "Officer", // Force new accounts to be standard officers
                ActiveStatus = true
            };
            newOfficer.PasswordHash = _passwordHasher.HashPassword(newOfficer, registerDto.Password);

            // 3. Save to SQL Server
            _context.Officers.Add(newOfficer);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful. You can now log in." });
        }

        // Accounts created before hashing was introduced still hold plaintext passwords.
        // Accept those once and upgrade them to a hash on successful login.
        private async Task<bool> VerifyPasswordAsync(Officer user, string password)
        {
            PasswordVerificationResult result;
            if (IsIdentityHash(user.PasswordHash))
            {
                result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            }
            else
            {
                result = IsLegacyPlaintextMatch(user.PasswordHash, password)
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;
            }

            if (result == PasswordVerificationResult.Failed) return false;

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, password);
                await _context.SaveChangesAsync();
            }

            return true;
        }

        // PasswordHasher output is base64 with a leading format marker (0x00 = v2, 0x01 = v3)
        private static bool IsIdentityHash(string stored)
        {
            var buffer = new byte[stored.Length];
            return Convert.TryFromBase64String(stored, buffer, out var length)
                && length >= 49
                && (buffer[0] == 0x00 || buffer[0] == 0x01);
        }

        private static bool IsLegacyPlaintextMatch(string stored, string password)
        {
            var storedBytes = Encoding.UTF8.GetBytes(stored);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            return CryptographicOperations.FixedTimeEquals(storedBytes, passwordBytes);
        }
    }
}
