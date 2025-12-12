using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supabase;
using CustomUser = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly Supabase.Client _supabase;

        public UsersController(Supabase.Client supabase) => _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var currentUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var currentUser = await _supabase.From<CustomUser>()
                                             .Where(u => u.Id == currentUserId)
                                             .Single();

            if (currentUser.Role == "admin")
            {
                var allUsers = await _supabase.From<CustomUser>().Get();
                return Ok(allUsers.Models);
            }

            return Ok(new[] { currentUser });
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CustomUser user)
        {
            var currentUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var currentUser = await _supabase.From<CustomUser>()
                                             .Where(u => u.Id == currentUserId)
                                             .Single();

            if (currentUser.Role != "admin")
                return Forbid();

            // Insert with generated Id and provided details
            var newUser = new CustomUser
            {
                Id = Guid.NewGuid().ToString(),  // Generate new UUID as string
                Email = user.Email,
                Role = user.Role,
                Name = user.Name,
                Surname = user.Surname
            };

            var response = await _supabase.From<CustomUser>().Insert(newUser);
            return Ok(response.Models.First());
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] CustomUser user)
        {
            var currentUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var currentUser = await _supabase.From<CustomUser>()
                                             .Where(u => u.Id == currentUserId)
                                             .Single();

            if (currentUser.Role != "admin" && currentUserId != id)
                return Forbid();

            await _supabase.From<CustomUser>().Where(u => u.Id == id).Update(user);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var currentUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var currentUser = await _supabase.From<CustomUser>()
                                             .Where(u => u.Id == currentUserId)
                                             .Single();

            if (currentUser.Role != "admin")
                return Forbid();

            await _supabase.From<CustomUser>().Where(u => u.Id == id).Delete();
            return Ok();
        }
    }
}