using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly Client _supabase;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;

        public AuthController(Client supabase, ILogger<AuthController> logger, IConfiguration configuration)
        {
            _supabase = supabase;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] SignupDto dto)
        {
            try
            {
                _logger.LogInformation("Signup attempt for: {Email}", dto.Email);

                // Check if user exists
                var existing = await _supabase
                    .From<User>()
                    .Where(u => u.Email == dto.Email)
                    .Get();

                if (existing.Models.Any())
                {
                    _logger.LogWarning("User already exists: {Email}", dto.Email);
                    return BadRequest(new { Message = "User already exists" });
                }

                // Hash password
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                // Create new user with Guid
                var newUser = new User
                {
                    Id = Guid.NewGuid(), // ✅ Now using Guid instead of string
                    Email = dto.Email,
                    Name = dto.Name,
                    Surname = dto.Surname,
                    Role = "user",
                    PasswordHash = hashedPassword
                };

                _logger.LogInformation("Inserting user with ID: {Id}", newUser.Id);

                // Insert user
                var response = await _supabase.From<User>().Insert(newUser);
                var createdUser = response.Models.FirstOrDefault();

                if (createdUser == null)
                {
                    _logger.LogError("User creation returned null");
                    return StatusCode(500, new { Message = "Failed to create user" });
                }

                _logger.LogInformation("User created successfully: {Email}", createdUser.Email);

                return Ok(new
                {
                    Message = "Signup successful",
                    User = new
                    {
                        createdUser.Id,
                        createdUser.Email,
                        createdUser.Name,
                        createdUser.Surname
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup failed: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    Message = "Signup failed",
                    Detail = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                _logger.LogInformation("Login attempt for: {Email}", dto.Email);

                var response = await _supabase
                    .From<User>()
                    .Where(u => u.Email == dto.Email)
                    .Get();

                var user = response.Models.FirstOrDefault();

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Email}", dto.Email);
                    return Unauthorized(new { Message = "Invalid credentials" });
                }

                if (string.IsNullOrEmpty(user.PasswordHash))
                {
                    _logger.LogError("User has no password hash: {Email}", dto.Email);
                    return Unauthorized(new { Message = "Invalid credentials" });
                }

                if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid password for: {Email}", dto.Email);
                    return Unauthorized(new { Message = "Invalid credentials" });
                }

                var tokenString = GenerateJwtToken(user);

                _logger.LogInformation("Login successful for: {Email}", dto.Email);

                return Ok(new
                {
                    Token = tokenString,
                    User = new
                    {
                        user.Id,
                        user.Email,
                        user.Name,
                        user.Surname,
                        user.Role
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    Message = "Login failed",
                    Detail = ex.Message
                });
            }
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration["Supabase:JwtSecret"]
                ?? throw new Exception("JWT Secret is missing");

            var key = Encoding.ASCII.GetBytes(secret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // ✅ Convert Guid to string
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role ?? "user"),
                    new Claim("role", user.Role ?? "user")
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }

    public class SignupDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}