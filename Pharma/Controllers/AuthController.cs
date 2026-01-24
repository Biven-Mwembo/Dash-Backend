using Microsoft.AspNetCore.Mvc;
using Supabase.Gotrue;

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
            try
            {
                var options = new SignUpOptions
                {
                    Data = new Dictionary<string, object>
                    {
                        { "name", request.Name ?? "" },
                        { "surname", request.Surname ?? "" },
                        { "role", "admin" } // Triggers public.users table insertion
                    }
                };

                var response = await _supabase.Auth.SignUp(request.Email, request.Password, options);

                if (response?.User != null)
                {
                    return Ok(new { success = true, message = "Compte créé avec succès." });
                }
                return BadRequest(new { success = false, message = "Échec de l'inscription." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup error for {Email}", request.Email);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                // In modern Supabase SDKs, SignInWithPassword returns the Session object directly
                var session = await _supabase.Auth.SignInWithPassword(request.Email, request.Password);

                if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                {
                    return Ok(new
                    {
                        success = true,
                        token = session.AccessToken,
                        message = "Connexion réussie."
                    });
                }
                return Unauthorized(new { message = "Identifiants invalides." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for {Email}", request.Email);
                return Unauthorized(new { message = "Email ou mot de passe incorrect." });
            }
        }
    }

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