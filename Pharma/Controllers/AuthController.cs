using Microsoft.AspNetCore.Mvc;
using Supabase.Gotrue;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<AuthController> _logger;

        public AuthController(Supabase.Client supabase, ILogger<AuthController> logger)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("signup")]
        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Email and Password are required." });
            }

            try
            {
                _logger.LogInformation("Attempting signup for email: {Email}", request.Email);

                var options = new SignUpOptions
                {
                    Data = new Dictionary<string, object>
                    {
                        { "name", request.Name ?? "" },
                        { "surname", request.Surname ?? "" },
                        { "role", "admin" } // Pass to metadata for the database trigger
                    }
                };

                var response = await _supabase.Auth.SignUp(request.Email, request.Password, options);

                // response.User might be non-null even if email confirmation is required
                if (response?.User != null)
                {
                    _logger.LogInformation("Signup successful for {Email}", request.Email);
                    return Ok(new
                    {
                        success = true,
                        message = "User created successfully. If email confirmation is enabled, please check your inbox."
                    });
                }

                return BadRequest(new { success = false, message = "Signup failed: No user returned." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup error for email: {Email}", request.Email);

                // Specific check for existing users
                if (ex.Message.Contains("already_exists") || ex.Message.Contains("already registered"))
                {
                    return Conflict(new { success = false, message = "A user with this email already exists." });
                }

                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Email and Password are required." });
            }

            try
            {
                _logger.LogInformation("Attempting login for email: {Email}", request.Email);

                // In most Supabase versions, 'response' here IS the Session object
                var session = await _supabase.Auth.SignInWithPassword(request.Email, request.Password);

                if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                {
                    _logger.LogInformation("Login successful for {Email}", request.Email);
                    return Ok(new
                    {
                        success = true,
                        token = session.AccessToken,
                        message = "Login successful."
                    });
                }

                return Unauthorized(new { success = false, message = "Invalid credentials." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for email: {Email}", request.Email);

                // Handle specific Supabase error messages
                if (ex.Message.Contains("Invalid login credentials"))
                {
                    return Unauthorized(new { success = false, message = "Email ou mot de passe incorrect." });
                }

                return StatusCode(500, new { success = false, message = "An internal error occurred." });
            }
        }
    }

    // --- Request Models ---

    public class SignUpRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}