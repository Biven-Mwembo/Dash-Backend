using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Postgrest; // Correct namespace for PostgrestException
using Supabase;
using Supabase.Postgrest.Exceptions;
using CustomUser = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly Supabase.Client _supabase;

        public UsersController(Supabase.Client supabase) =>
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var currentUserId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized();

                var currentUserResponse = await _supabase.From<CustomUser>()
                                                         .Where(u => u.Id == currentUserId)
                                                         .Get();
                var currentUser = currentUserResponse.Models.FirstOrDefault();
                if (currentUser == null)
                    return Unauthorized();

                if (currentUser.Role == "admin")
                {
                    var allUsersResponse = await _supabase.From<CustomUser>().Get();
                    var users = allUsersResponse.Models ?? new List<CustomUser>();
                    return Ok(users);
                }

                return Ok(new[] { currentUser });
            }
            catch (PostgrestException pe)
            {
                return StatusCode(500, $"Postgrest error retrieving users: {pe.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving users: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CustomUser user)
        {
            try
            {
                var currentUserId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized();

                var currentUserResponse = await _supabase.From<CustomUser>()
                                                         .Where(u => u.Id == currentUserId)
                                                         .Get();
                var currentUser = currentUserResponse.Models.FirstOrDefault();
                if (currentUser == null)
                    return Unauthorized();

                if (currentUser.Role != "admin")
                    return Forbid();

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
                return StatusCode(403, "Permission Denied: RLS policy violated for users table.");
            }
            catch (PostgrestException pe)
            {
                return StatusCode(500, $"Postgrest error creating user: {pe.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating user: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] CustomUser user)
        {
            try
            {
                var currentUserId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized();

                var currentUserResponse = await _supabase.From<CustomUser>()
                                                         .Where(u => u.Id == currentUserId)
                                                         .Get();
                var currentUser = currentUserResponse.Models.FirstOrDefault();
                if (currentUser == null)
                    return Unauthorized();

                if (currentUser.Role != "admin" && currentUserId != id)
                    return Forbid();

                await _supabase.From<CustomUser>().Where(u => u.Id == id).Update(user);
                return Ok();
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                return StatusCode(403, "Permission Denied: RLS policy violated for users table.");
            }
            catch (PostgrestException pe)
            {
                return StatusCode(500, $"Postgrest error updating user {id}: {pe.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating user {id}: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                var currentUserId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized();

                var currentUserResponse = await _supabase.From<CustomUser>()
                                                         .Where(u => u.Id == currentUserId)
                                                         .Get();
                var currentUser = currentUserResponse.Models.FirstOrDefault();
                if (currentUser == null)
                    return Unauthorized();

                if (currentUser.Role != "admin")
                    return Forbid();

                await _supabase.From<CustomUser>().Where(u => u.Id == id).Delete();
                return Ok();
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                return StatusCode(403, "Permission Denied: RLS policy violated for users table.");
            }
            catch (PostgrestException pe)
            {
                return StatusCode(500, $"Postgrest error deleting user {id}: {pe.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting user {id}: {ex.Message}");
            }
        }
    }
}
