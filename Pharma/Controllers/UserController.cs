using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supabase;
using System.Security.Claims;
using CustomUser = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<UsersController> _logger;

        public UsersController(Supabase.Client supabase, ILogger<UsersController> logger)
        {
            _supabase = supabase;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            // Extract the user ID (Subject) from the token claims
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // ✅ Check role directly from the JWT (no extra DB call needed)
            if (User.IsInRole("admin"))
            {
                var response = await _supabase.From<CustomUser>().Get();
                return Ok(response.Models ?? new List<CustomUser>());
            }

            // If not admin, only return the current user's own record
            var singleResponse = await _supabase.From<CustomUser>()
                .Where(u => u.Id == currentUserId)
                .Get();

            return Ok(singleResponse.Models);
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var response = await _supabase.From<CustomUser>().Where(u => u.Id == userId).Single();
            return Ok(response);
        }
    }
}