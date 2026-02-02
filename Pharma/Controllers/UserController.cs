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
            try
            {
                // Extract the user ID from the token claims
                var currentUserIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(currentUserIdString))
                {
                    _logger.LogWarning("No user ID found in token");
                    return Unauthorized(new { Message = "Invalid token" });
                }

                // ✅ Parse string to Guid
                if (!Guid.TryParse(currentUserIdString, out Guid currentUserId))
                {
                    _logger.LogError("Invalid user ID format: {UserId}", currentUserIdString);
                    return BadRequest(new { Message = "Invalid user ID format" });
                }

                // Check role directly from the JWT
                if (User.IsInRole("admin"))
                {
                    _logger.LogInformation("Admin user {UserId} requesting all users", currentUserId);
                    var response = await _supabase.From<CustomUser>().Get();
                    return Ok(response.Models ?? new List<CustomUser>());
                }

                // If not admin, only return the current user's own record
                _logger.LogInformation("User {UserId} requesting own profile", currentUserId);
                var singleResponse = await _supabase
                    .From<CustomUser>()
                    .Where(u => u.Id == currentUserId)
                    .Get();

                return Ok(singleResponse.Models);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users");
                return StatusCode(500, new { Message = "Error fetching users", Detail = ex.Message });
            }
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            try
            {
                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdString))
                {
                    _logger.LogWarning("No user ID found in token");
                    return Unauthorized(new { Message = "Invalid token" });
                }

                // ✅ Parse string to Guid
                if (!Guid.TryParse(userIdString, out Guid userId))
                {
                    _logger.LogError("Invalid user ID format: {UserId}", userIdString);
                    return BadRequest(new { Message = "Invalid user ID format" });
                }

                _logger.LogInformation("Fetching profile for user: {UserId}", userId);

                var response = await _supabase
                    .From<CustomUser>()
                    .Where(u => u.Id == userId)
                    .Single();

                if (response == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    return NotFound(new { Message = "User not found" });
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user profile");
                return StatusCode(500, new { Message = "Error fetching profile", Detail = ex.Message });
            }
        }

        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileDto dto)
        {
            try
            {
                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdString))
                {
                    return Unauthorized(new { Message = "Invalid token" });
                }

                if (!Guid.TryParse(userIdString, out Guid userId))
                {
                    return BadRequest(new { Message = "Invalid user ID format" });
                }

                _logger.LogInformation("Updating profile for user: {UserId}", userId);

                // Fetch current user
                var currentUser = await _supabase
                    .From<CustomUser>()
                    .Where(u => u.Id == userId)
                    .Single();

                if (currentUser == null)
                {
                    return NotFound(new { Message = "User not found" });
                }

                // Update fields
                if (!string.IsNullOrEmpty(dto.Name))
                    currentUser.Name = dto.Name;

                if (!string.IsNullOrEmpty(dto.Surname))
                    currentUser.Surname = dto.Surname;

                // Update in database
                await _supabase.From<CustomUser>().Update(currentUser);

                _logger.LogInformation("Profile updated successfully for user: {UserId}", userId);

                return Ok(new
                {
                    Message = "Profile updated successfully",
                    User = new
                    {
                        currentUser.Id,
                        currentUser.Email,
                        currentUser.Name,
                        currentUser.Surname,
                        currentUser.Role
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile");
                return StatusCode(500, new { Message = "Error updating profile", Detail = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                // ✅ Parse string to Guid
                if (!Guid.TryParse(id, out Guid userId))
                {
                    _logger.LogError("Invalid user ID format: {UserId}", id);
                    return BadRequest(new { Message = "Invalid user ID format" });
                }

                _logger.LogInformation("Admin deleting user: {UserId}", userId);

                await _supabase
                    .From<CustomUser>()
                    .Where(u => u.Id == userId)
                    .Delete();

                _logger.LogInformation("User deleted successfully: {UserId}", userId);

                return Ok(new { Message = "User deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user");
                return StatusCode(500, new { Message = "Error deleting user", Detail = ex.Message });
            }
        }
    }

    // DTO for updating profile
    public class UpdateProfileDto
    {
        public string? Name { get; set; }
        public string? Surname { get; set; }
    }
}