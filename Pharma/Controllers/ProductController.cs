using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.Security.Claims;

// Aliases for local clarity
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;
using User = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/products")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(Supabase.Client supabase, ILogger<ProductsController> logger)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Check if the user has the 'admin' role in the public.users table.
        /// </summary>
        private async Task<bool> IsUserAdmin()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Admin check failed: No user ID found in token.");
                return false;
            }

            try
            {
                // Note: Ensure RLS allows the authenticated user to read their own row in public.users
                var result = await _supabase.From<User>()
                    .Where(u => u.Id == userId)
                    .Get();

                var userRecord = result.Models.FirstOrDefault();

                if (userRecord == null)
                {
                    _logger.LogWarning("Admin check: User {UserId} not found in public.users table.", userId);
                    return false;
                }

                bool isAdmin = userRecord.Role?.ToLower() == "admin";
                _logger.LogInformation("Role check for {Email}: {Role} (IsAdmin: {IsAdmin})", userRecord.Email, userRecord.Role, isAdmin);

                return isAdmin;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying public.users for admin check.");
                return false;
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                var response = await _supabase.From<Product>().Get();
                var products = response.Models ?? new List<Product>();
                return Ok(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching products.");
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("sales")]
        public async Task<IActionResult> GetProductSales()
        {
            try
            {
                var response = await _supabase.From<Sale>().Get();
                var saleDtos = response.Models.Select(s => new SaleDto
                {
                    Id = s.Id,
                    ProductId = s.ProductId,
                    QuantitySold = s.QuantitySold,
                    SaleDate = s.SaleDate
                }).ToList();

                return Ok(saleDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching sales.");
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                var response = await _supabase.From<Sale>().Get();
                var dailyTotals = response.Models
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new
                    {
                        Date = g.Key.ToString("yyyy-MM-dd"),
                        Count = g.Sum(s => s.QuantitySold)
                    })
                    .OrderBy(x => x.Date);

                return Ok(dailyTotals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDailySales.");
                return StatusCode(500, "Error fetching stats.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
            // Verify admin status before allowing creation
            if (!await IsUserAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to create product.");
                return Forbid();
            }

            try
            {
                var product = new Product
                {
                    ProductCode = string.IsNullOrEmpty(productDto.ProductCode)
                        ? await GenerateNextProductCode()
                        : productDto.ProductCode,
                    Name = productDto.Name,
                    Quantity = productDto.Quantity,
                    Price = productDto.Price,
                    PrixAchat = productDto.PrixAchat,
                    SupplierId = productDto.SupplierId
                };

                var response = await _supabase.From<Product>().Insert(product);
                return Ok(response.Models.First());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product.");
                return StatusCode(500, "Product creation failed.");
            }
        }

        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            if (items == null || !items.Any()) return BadRequest("Basket is empty.");

            try
            {
                foreach (var item in items)
                {
                    var product = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();

                    if (product == null) return NotFound($"Product {item.ProductId} not found.");
                    if (product.Quantity < item.QuantitySold) return BadRequest($"Insufficient stock for {product.Name}.");

                    // Update Stock
                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);

                    // Record Sale
                    await _supabase.From<Sale>().Insert(new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    });
                }
                return Ok(new { message = "Sale processed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sale processing error.");
                return StatusCode(500, "Internal error during sale.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid();

            try
            {
                var update = new Product
                {
                    Id = id,
                    Name = productDto.Name,
                    Quantity = productDto.Quantity,
                    Price = productDto.Price,
                    PrixAchat = productDto.PrixAchat,
                    SupplierId = productDto.SupplierId,
                    ProductCode = productDto.ProductCode
                };
                await _supabase.From<Product>().Where(p => p.Id == id).Update(update);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            if (!await IsUserAdmin()) return Forbid();

            try
            {
                await _supabase.From<Product>().Where(p => p.Id == id).Delete();
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private async Task<string> GenerateNextProductCode()
        {
            var result = await _supabase.From<Product>()
                .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1).Get();

            if (result.Models.Any())
            {
                var lastCode = result.Models.First().ProductCode;
                if (int.TryParse(lastCode?.Replace("PR", ""), out var num))
                {
                    return $"PR{(num + 1):D3}";
                }
            }
            return "PR001";
        }
    }

    // DTO kept in same file for compilation convenience
    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }
}