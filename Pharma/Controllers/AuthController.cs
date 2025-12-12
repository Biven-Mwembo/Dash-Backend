using Microsoft.AspNetCore.Mvc;
using Supabase;
using Supabase.Gotrue;
using Postgrest;

namespace Pharma.Controllers
{
    using CustomUser = Pharma.Models.User;

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly Supabase.Client _supabase;

        public AuthController(Supabase.Client supabase) =>
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));

        [HttpPost("signup")]
        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
        {
            if (request.Email is null || request.Password is null || request.Name is null || request.Surname is null)
                return BadRequest("Email, Password, Name, and Surname are required.");

            try
            {
                // ✅ Use user_metadata to pass Name and Surname
                var response = await _supabase.Auth.SignUp(
    request.Email,
    request.Password,
    new SignUpOptions
    {
        Data = new Dictionary<string, object>
        {
            { "name", request.Name ?? "" },
            { "surname", request.Surname ?? "" }
        }
    });


                

                if (response.User != null)
                {
                   

                    return Ok(new
                    {
                        Token = response.AccessToken,
                        Message = "User created successfully. Name and Surname will appear in public.users via trigger."
                    });
                }

                // Handle cases like email confirmation required
                return Ok(new
                {
                    Message = "SignUp initiated. Please confirm your email before logging in."
                });
            }
            catch (Exception ex)
            {
                // Handle user already exists error
                if (ex.Message.Contains("user_already_exists"))
                    return BadRequest("User already registered. Please login instead.");

                Console.WriteLine($"SignUp error: {ex.Message}");
                return BadRequest($"SignUp failed: {ex.Message}");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request.Email is null || request.Password is null)
                return BadRequest("Email and Password are required.");

            try
            {
                var response = await _supabase.Auth.SignInWithPassword(request.Email, request.Password);

                if (response.User != null)
                    return Ok(new { Token = response.AccessToken });

                return BadRequest("Login failed.");
            }
            catch (Exception ex)
            {
               
                return BadRequest($"Login failed: {ex.Message}");
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
