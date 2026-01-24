using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.Security.Claims;

// Aliases for local clarity to avoid conflicts
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

        private async Task<bool> IsUserAdmin()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId)) return false;

            try
            {
                // Fetch the user from public.users table based on their Supabase UUID (stored as string)
                var result = await _supabase.From<User>().Where(u => u.Id == userId).Get();
                var userRecord = result.Models.FirstOrDefault();

                if (userRecord == null) return false;

                return userRecord.Role?.ToLower() == "admin";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin check failed for User {Id}", userId);
                return false;
            }
        }

        [HttpGet]
        [Authorize] // Any valid user can call this
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                var response = await _supabase.From<Product>().Get();
                return Ok(response.Models ?? new List<Product>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching products.");
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            var response = await _supabase.From<Sale>().Get();
            var dailyTotals = (response.Models ?? new List<Sale>())
                .GroupBy(s => s.SaleDate.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Count = g.Sum(s => s.QuantitySold)
                })
                .OrderBy(x => x.Date);

            return Ok(dailyTotals);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto dto)
        {
            if (!await IsUserAdmin()) return Forbid();

            var product = new Product
            {
                ProductCode = string.IsNullOrEmpty(dto.ProductCode) ? await GenerateNextProductCode() : dto.ProductCode,
                Name = dto.Name,
                Quantity = dto.Quantity,
                Price = dto.Price,
                PrixAchat = dto.PrixAchat,
                SupplierId = dto.SupplierId
            };

            var res = await _supabase.From<Product>().Insert(product);
            return Ok(res.Models.First());
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
                    if (product == null || product.Quantity < item.QuantitySold)
                        return BadRequest($"Stock issue for Product ID: {item.ProductId}");

                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);

                    await _supabase.From<Sale>().Insert(new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    });
                }
                return Ok(new { message = "Vente réussie." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sale processing error.");
                return StatusCode(500, "Internal Server Error.");
            }
        }

        private async Task<string> GenerateNextProductCode()
        {
            var res = await _supabase.From<Product>()
                .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1).Get();

            if (res.Models.Any())
            {
                var lastCode = res.Models.First().ProductCode;
                if (int.TryParse(lastCode?.Replace("PR", ""), out var num))
                    return $"PR{(num + 1):D3}";
            }
            return "PR001";
        }
    }

    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }
}