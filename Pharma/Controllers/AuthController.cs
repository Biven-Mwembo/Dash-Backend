using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

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
                _logger.LogInformation("Signup attempt for email: {Email}", dto.Email);

                // 1. Create Supabase Auth user
                var signUpResponse = await _supabase.Auth.SignUp(dto.Email, dto.Password);

                if (signUpResponse?.User == null)
                {
                    return BadRequest(new { Message = "Failed to create authentication user" });
                }

                var supabaseUserId = signUpResponse.User.Id;
                _logger.LogInformation("Supabase user created with ID: {UserId}", supabaseUserId);

                // 2. Create user record in users table
                var newUser = new User
                {
                    Id = supabaseUserId,
                    Email = dto.Email,
                    Name = dto.Name,
                    Surname = dto.Surname,
                    Role = "user" // ✅ Default role
                };

                var insertResponse = await _supabase.From<User>().Insert(newUser);
                var createdUser = insertResponse.Models.FirstOrDefault();

                if (createdUser == null)
                {
                    return StatusCode(500, new { Message = "User created in auth but failed to save to database" });
                }

                _logger.LogInformation("User record created in database with ID: {UserId}", createdUser.Id);

                return Ok(new
                {
                    Message = "Signup successful. Please check your email to verify your account.",
                    User = new
                    {
                        createdUser.Id,
                        createdUser.Email,
                        createdUser.Name,
                        createdUser.Surname,
                        createdUser.Role
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup failed for email: {Email}", dto.Email);
                return StatusCode(500, new { Message = "Signup failed", Detail = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                _logger.LogInformation("Login attempt for email: {Email}", dto.Email);

                // 1. Authenticate with Supabase
                var session = await _supabase.Auth.SignIn(dto.Email, dto.Password);

                if (session?.AccessToken == null)
                {
                    return Unauthorized(new { Message = "Invalid email or password" });
                }

                // 2. Get user from database to retrieve role
                var userResponse = await _supabase
                    .From<User>()
                    .Where(u => u.Email == dto.Email)
                    .Single();

                if (userResponse == null)
                {
                    return Unauthorized(new { Message = "User not found in database" });
                }

                _logger.LogInformation("User {Email} logged in successfully with role: {Role}", dto.Email, userResponse.Role);

                // 3. Create custom JWT with role claim
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? "your-secret-key-must-be-at-least-32-characters-long-for-security!");

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userResponse.Id),
                        new Claim(ClaimTypes.Email, userResponse.Email),
                        new Claim(ClaimTypes.Name, $"{userResponse.Name} {userResponse.Surname}"),
                        new Claim(ClaimTypes.Role, userResponse.Role ?? "user"),
                        new Claim("role", userResponse.Role ?? "user"),
                        new Claim("user_id", userResponse.Id)
                    }),
                    Expires = DateTime.UtcNow.AddDays(7),
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(key),
                        SecurityAlgorithms.HmacSha256Signature
                    )
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                return Ok(new
                {
                    Token = tokenString,
                    User = new
                    {
                        userResponse.Id,
                        userResponse.Email,
                        userResponse.Name,
                        userResponse.Surname,
                        userResponse.Role
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for email: {Email}", dto.Email);
                return Unauthorized(new { Message = "Login failed", Detail = ex.Message });
            }
        }
    }

    // DTOs
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