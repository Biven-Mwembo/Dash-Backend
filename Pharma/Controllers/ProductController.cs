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
    [Authorize] // This ensures a valid Supabase JWT is present
    public class ProductsController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(Supabase.Client supabase, ILogger<ProductsController> logger)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // --- HELPER: Admin Check ---
        // Used only for sensitive operations (Create, Update, Delete)
        private async Task<bool> IsUserAdmin()
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId)) return false;

                var user = await _supabase.From<User>().Where(u => u.Id == userId).Single();
                return user?.Role?.ToLower() == "admin";
            }
            catch
            {
                return false;
            }
        }

        // --- GET ALL PRODUCTS ---
        // Access: Any Authenticated User
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                _logger.LogInformation("Fetching products for POS.");
                var response = await _supabase.From<Product>().Get();
                var products = response.Models ?? new List<Product>();

                var productDtos = products.Select(p => new ProductDto
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
                return StatusCode(500, "Internal Server Error");
            }
        }

        // --- GET ALL SALES ---
        // Access: Any Authenticated User
        [HttpGet("sales")]
        public async Task<IActionResult> GetProductSales()
        {
            try
            {
                _logger.LogInformation("Fetching sales history.");
                // We perform a join logic or simple fetch
                var response = await _supabase.From<Sale>().Get();
                var sales = response.Models ?? new List<Sale>();

                // Your frontend expects specific fields like productName
                // If your Sale model doesn't have it, we map it here
                return Ok(sales);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching sales.");
                return StatusCode(500, "Internal Server Error");
            }
        }

        // --- CREATE PRODUCT ---
        // Access: Admin Only
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
            if (!await IsUserAdmin()) return Forbid("Admin access required.");

            try
            {
                var product = new Product
                {
                    ProductCode = !string.IsNullOrEmpty(productDto.ProductCode)
                                  ? productDto.ProductCode
                                  : await GenerateNextProductCode(),
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
                return StatusCode(500, "Error creating product.");
            }
        }

        // --- UPDATE PRODUCT ---
        // Access: Admin Only
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product {Id}", id);
                return StatusCode(500, "Update failed.");
            }
        }

        // --- DELETE PRODUCT ---
        // Access: Admin Only
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
                _logger.LogError(ex, "Error deleting product {Id}", id);
                return StatusCode(500, "Deletion failed.");
            }
        }

        // --- RECORD A SALE (POS) ---
        // Access: Any Authenticated User
        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleRequest> saleItems)
        {
            try
            {
                foreach (var item in saleItems)
                {
                    // 1. Get current product
                    var product = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();

                    if (product.Quantity < item.QuantitySold)
                        return BadRequest($"Stock insuffisant pour {product.Name}");

                    // 2. Update Stock
                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);

                    // 3. Record Sale
                    var sale = new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    };
                    await _supabase.From<Sale>().Insert(sale);
                }
                return Ok("Vente réussie");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vente");
                return StatusCode(500, "Erreur lors du traitement de la vente");
            }
        }

        // --- GENERATE CODE HELPER ---
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

    // --- SUPPORTING DTOS ---
    public class SaleRequest
    {
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
    }
}