using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;  // ✅ Added for logging
using Postgrest;
using Supabase;
using Supabase.Gotrue;

namespace Pharma.Controllers
{
    using CustomUser = Pharma.Models.User;

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<AuthController> _logger;  // ✅ Added logger for better error tracking

        public AuthController(Supabase.Client supabase, ILogger<AuthController> logger) =>
            (_supabase, _logger) = (supabase ?? throw new ArgumentNullException(nameof(supabase)), logger ?? throw new ArgumentNullException(nameof(logger)));

        [HttpPost("signup")]
        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Surname))
            {
                return BadRequest(new { success = false, message = "Email, Password, Name, and Surname are required." });
            }

            try
            {
                var response = await _supabase.Auth.SignUp(
                    request.Email,
                    request.Password,
                    new SignUpOptions
                    {
                        Data = new Dictionary<string, object>
                        {
                            { "name", request.Name },
                            { "surname", request.Surname }
                        }
                    });

                if (response.User != null && !string.IsNullOrEmpty(response.AccessToken))
                {
                    return Ok(new
                    {
                        success = true,
                        token = response.AccessToken,
                        message = "User created successfully. Name and Surname will appear in public.users via trigger."
                    });
                }

                // Handle cases like email confirmation required
                return Ok(new
                {
                    success = false,
                    message = "SignUp initiated. Please confirm your email before logging in."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignUp error for email: {Email}", request.Email);  // ✅ Added logging

                if (ex.Message.Contains("user_already_exists") || ex.Message.Contains("User already registered"))
                {
                    return Conflict(new { success = false, message = "User already registered. Please login instead." });  // ✅ Changed to 409 Conflict
                }

                return StatusCode(500, new { success = false, message = "SignUp failed due to server error." });
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
                var response = await _supabase.Auth.SignInWithPassword(request.Email, request.Password);

                if (response.User != null && !string.IsNullOrEmpty(response.AccessToken))
                {
                    return Ok(new { success = true, token = response.AccessToken, message = "Login successful." });
                }

                // Login failed (invalid credentials)
                return Unauthorized(new { success = false, message = "Invalid email or password." });  // ✅ Changed to 401 Unauthorized
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for email: {Email}", request.Email);  // ✅ Added logging
                return StatusCode(500, new { success = false, message = "Login failed due to server error." });
            }
        }
    }

    public class SignUpRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Name { get; set; }
        public string? Surname { get; set; }
    }

    public class LoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}