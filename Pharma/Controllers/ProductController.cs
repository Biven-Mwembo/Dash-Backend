using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using Supabase.Postgrest.Exceptions;
using System.Security.Claims;

// Aliases to avoid conflicts with built-in classes
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;
using User = Pharma.Models.User;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/products")]
    [Authorize] // Requires valid Supabase JWT
    public class ProductsController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(Supabase.Client supabase, ILogger<ProductsController> logger)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // --- Helper: Check if current user is admin ---
        private async Task<bool> IsUserAdmin()
        {
            // Gets the UUID string from the 'sub' claim in the JWT
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId)) return false;

            try
            {
                // Matches your Model: Id is a string (UUID)
                var user = await _supabase.From<User>().Where(u => u.Id == userId).Single();
                return user?.Role?.ToLower() == "admin";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Admin check failed for user {UserId}: {Message}", userId, ex.Message);
                return false;
            }
        }

        // GET: api/products
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                _logger.LogInformation("Fetching products.");
                var productsResponse = await _supabase.From<Product>().Get();
                var productDtos = (productsResponse.Models ?? new List<Product>())
                    .Select(p => new ProductDto
                    {
                        Id = p.Id,
                        ProductCode = p.ProductCode,
                        Name = p.Name,
                        Quantity = p.Quantity,
                        Price = p.Price,
                        PrixAchat = p.PrixAchat,
                        SupplierId = p.SupplierId,
                        CreatedAt = p.CreatedAt
                    }).ToList();

                return Ok(productDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching products.");
                return StatusCode(500, "Error retrieving products.");
            }
        }

        // GET: api/products/sales
        [HttpGet("sales")]
        public async Task<IActionResult> GetProductSales()
        {
            try
            {
                _logger.LogInformation("Fetching sales history.");
                var salesResponse = await _supabase.From<Sale>().Get();
                var saleDtos = (salesResponse.Models ?? new List<Sale>())
                    .Select(s => new SaleDto
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
                return StatusCode(500, "Error retrieving sales history.");
            }
        }

        // GET: api/products/sales/daily
        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                _logger.LogInformation("Fetching daily sales statistics.");
                var response = await _supabase.From<Sale>().Get();
                var sales = response.Models ?? new List<Sale>();

                var dailyTotals = sales
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new
                    {
                        Date = g.Key.ToString("yyyy-MM-dd"),
                        Count = g.Sum(s => s.QuantitySold)
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                return Ok(dailyTotals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDailySales.");
                return StatusCode(500, "Error fetching daily statistics.");
            }
        }

        // POST: api/products
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid();

            try
            {
                var product = new Product
                {
                    ProductCode = !string.IsNullOrEmpty(productDto.ProductCode) ? productDto.ProductCode : await GenerateNextProductCode(),
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
                return StatusCode(500, "Creation failed.");
            }
        }

        // POST: api/products/sale
        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            if (items == null || !items.Any()) return BadRequest("Panier vide.");

            try
            {
                foreach (var item in items)
                {
                    var productResponse = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();
                    if (productResponse == null) return NotFound($"Produit {item.ProductId} non trouvé.");
                    if (productResponse.Quantity < item.QuantitySold) return BadRequest($"Stock insuffisant pour {productResponse.Name}.");

                    productResponse.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(productResponse);

                    var saleRecord = new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    };
                    await _supabase.From<Sale>().Insert(saleRecord);
                }
                return Ok(new { message = "Vente réussie." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sale process error.");
                return StatusCode(500, "Internal Server Error.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid();
            try
            {
                var product = new Product
                {
                    Id = id,
                    ProductCode = productDto.ProductCode,
                    Name = productDto.Name,
                    Quantity = productDto.Quantity,
                    Price = productDto.Price,
                    PrixAchat = productDto.PrixAchat,
                    SupplierId = productDto.SupplierId
                };
                await _supabase.From<Product>().Where(p => p.Id == id).Update(product);
                return Ok();
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
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
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        private async Task<string> GenerateNextProductCode()
        {
            var products = await _supabase.From<Product>()
                .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1).Get();

            if (products.Models.Any())
            {
                var lastCode = products.Models.First().ProductCode;
                if (int.TryParse(lastCode?.Replace("PR", ""), out var num))
                {
                    return $"PR{(num + 1):D3}";
                }
            }
            return "PR001";
        }
    }

    // --- Added missing DTO to fix compilation error ---
    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }
}