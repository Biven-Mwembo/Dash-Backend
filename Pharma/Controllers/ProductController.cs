using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pharma.Models;
using Supabase;
using Supabase.Postgrest.Exceptions;
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
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId)) return false;

            try
            {
                // Note: Ensure your 'User' table in Supabase allows authenticated users to read roles
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
                        Id = (int)s.Id,
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

        // POST: api/products
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid("Seuls les administrateurs peuvent ajouter des produits.");

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

        // PUT: api/products/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid("Seuls les administrateurs peuvent modifier les produits.");

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update error.");
                return StatusCode(500, "Update failed.");
            }
        }

        // DELETE: api/products/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            if (!await IsUserAdmin()) return Forbid("Seuls les administrateurs peuvent supprimer des produits.");

            try
            {
                await _supabase.From<Product>().Where(p => p.Id == id).Delete();
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete error.");
                return StatusCode(500, "Delete failed.");
            }
        }

        private async Task<string> GenerateNextProductCode()
        {
            var products = await _supabase.From<Product>()
                .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1)
                .Get();

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

    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }
}