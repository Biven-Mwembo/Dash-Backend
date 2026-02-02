using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net; // This will light up now!

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
                // 1. Check if user already exists
                var existing = await _supabase.From<User>().Where(u => u.Email == dto.Email).Get();
                if (existing.Models.Any())
                {
                    return BadRequest(new { Message = "User already exists" });
                }

                // 2. Hash the password using BCrypt
                // This call makes the 'using' statement active
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                // 3. Create User Object
                var newUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = dto.Email,
                    Name = dto.Name,
                    Surname = dto.Surname,
                    Role = "user",
                    PasswordHash = hashedPassword
                };

                // 4. Insert into 'users' table
                var response = await _supabase.From<User>().Insert(newUser);
                var createdUser = response.Models.FirstOrDefault();

                return Ok(new { Message = "Signup successful", User = new { createdUser?.Email, createdUser?.Name } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup failed");
                return StatusCode(500, new { Message = "Signup failed", Detail = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                // 1. Get user by Email
                var response = await _supabase
                    .From<User>()
                    .Where(u => u.Email == dto.Email)
                    .Get();

                var user = response.Models.FirstOrDefault();

                // 2. Check user existence and Verify Password Hash
                // Use the explicit check to avoid null reference on PasswordHash
                if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                {
                    return Unauthorized(new { Message = "Invalid credentials" });
                }

                // 3. Generate Token
                var tokenString = GenerateJwtToken(user);

                return Ok(new
                {
                    Token = tokenString,
                    User = new { user.Id, user.Email, user.Name, user.Surname, user.Role }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed");
                return StatusCode(500, new { Message = "Login failed", Detail = ex.Message });
            }
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration["Supabase:JwtSecret"];

            if (string.IsNullOrEmpty(secret))
                throw new Exception("JWT Secret is missing in configuration.");

            var key = Encoding.ASCII.GetBytes(secret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
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