using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Postgrest; // Correct namespace for PostgrestException
using Supabase;
using Supabase.Postgrest.Exceptions;
using Microsoft.Extensions.Logging;  // ✅ Added for logging
using CustomUser = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<UsersController> _logger;  // ✅ Added logger

        public UsersController(Supabase.Client supabase, ILogger<UsersController> logger) =>
            (_supabase, _logger) = (supabase ?? throw new ArgumentNullException(nameof(supabase)), logger ?? throw new ArgumentNullException(nameof(logger)));

        // ✅ Helper: Get current user (cached to avoid repeated DB calls)
        private async Task<(CustomUser? user, IActionResult? error)> GetCurrentUser()
        {
            var currentUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                _logger.LogWarning("Unauthorized: No user ID in token.");
                return (null, Unauthorized("Invalid token."));
            }

            try
            {
                var response = await _supabase.From<CustomUser>().Where(u => u.Id == currentUserId).Get();
                var user = response.Models.FirstOrDefault();
                if (user == null)
                {
                    _logger.LogWarning("User not found in DB for ID: {UserId}", currentUserId);
                    return (null, Unauthorized("User not found."));
                }
                return (user, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching current user for ID: {UserId}", currentUserId);
                return (null, StatusCode(500, "Error retrieving user data."));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var (currentUser, error) = await GetCurrentUser();
            if (error != null) return error;

            try
            {
                if (currentUser!.Role == "admin")
                {
                    var allUsersResponse = await _supabase.From<CustomUser>().Get();
                    var users = allUsersResponse.Models ?? new List<CustomUser>();
                    return Ok(users);
                }

                return Ok(new[] { currentUser });
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error retrieving users");
                return StatusCode(500, $"Database error: {pe.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving users");
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CustomUser user)
        {
            var (currentUser, error) = await GetCurrentUser();
            if (error != null) return error;

            if (currentUser!.Role != "admin")
                return Forbid("Admin access required.");

            try
            {
                var newUser = new CustomUser
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = user.Email,
                    Role = user.Role,
                    Name = user.Name,
                    Surname = user.Surname
                };

                var response = await _supabase.From<CustomUser>().Insert(newUser);
                var createdUser = response.Models.FirstOrDefault();
                return Ok(createdUser);
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation creating user");
                return StatusCode(403, "Permission denied: RLS policy violated.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error creating user: {Message}", pe.Message);
                return StatusCode(500, $"Database error: {pe.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating user");
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] CustomUser user)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest("User ID required.");

            var (currentUser, error) = await GetCurrentUser();
            if (error != null) return error;

            if (currentUser!.Role != "admin" && currentUser.Id != id)
                return Forbid("Admin access or self-update required.");

            try
            {
                await _supabase.From<CustomUser>().Where(u => u.Id == id).Update(user);
                return Ok();
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation updating user {Id}", id);
                return StatusCode(403, "Permission denied: RLS policy violated.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error updating user {Id}: {Message}", id, pe.Message);
                return StatusCode(500, $"Database error: {pe.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating user {Id}", id);
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest("User ID required.");

            var (currentUser, error) = await GetCurrentUser();
            if (error != null) return error;

            if (currentUser!.Role != "admin")
                return Forbid("Admin access required.");

            try
            {
                await _supabase.From<CustomUser>().Where(u => u.Id == id).Delete();
                return Ok();
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation deleting user {Id}", id);
                return StatusCode(403, "Permission denied: RLS policy violated.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error deleting user {Id}: {Message}", id, pe.Message);
                return StatusCode(500, $"Database error: {pe.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting user {Id}", id);
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }
    }
}
