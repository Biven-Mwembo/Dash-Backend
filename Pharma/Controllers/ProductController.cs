using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pharma.Models;
using Supabase;
using Supabase.Postgrest.Exceptions;
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;
using User = Pharma.Models.User; // Assuming this model exists; add if needed

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

        // Helper: Check if current user is admin
        private async Task<bool> IsUserAdmin(string userId)
        {
            try
            {
                var user = await _supabase.From<User>().Where(u => u.Id == userId).Single();
                return user?.Role == "admin"; // Adjust to 'IsAdmin' if boolean
            }
            catch
            {
                return false; // Deny if query fails
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var userId = User.FindFirst("sub")?.Value; // JWT user ID
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            try
            {
                _logger.LogInformation("Fetching all products for user.");
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

                _logger.LogInformation("Fetched {Count} products successfully.", productDtos.Count);
                return Ok(productDtos);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error fetching products: {Message}", pe.Message);
                return StatusCode(500, "Error fetching products. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching products: {Message}", ex.Message);
                return StatusCode(500, "Error fetching products. Check logs.");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            if (id <= 0) return BadRequest("Invalid product ID.");

            try
            {
                _logger.LogInformation("Fetching product with ID {Id}.", id);
                Product product = null;
                try
                {
                    product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
                }
                catch (PostgrestException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(ex, "Product with ID {Id} not found.", id);
                    return NotFound($"Product with ID {id} not found.");
                }

                var productDto = new ProductDto
                {
                    Id = product.Id,
                    ProductCode = product.ProductCode,
                    Name = product.Name,
                    Quantity = product.Quantity,
                    Price = product.Price,
                    PrixAchat = product.PrixAchat,
                    SupplierId = product.SupplierId,
                    CreatedAt = product.CreatedAt
                };

                _logger.LogInformation("Fetched product {Id} successfully.", id);
                return Ok(productDto);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error retrieving product {Id}: {Message}", id, pe.Message);
                return StatusCode(500, $"Error retrieving product {id}. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving product {Id}: {Message}", id, ex.Message);
                return StatusCode(500, $"Error retrieving product {id}. Check logs.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            if (productDto == null || string.IsNullOrWhiteSpace(productDto.Name) || productDto.Quantity < 0)
                return BadRequest("Invalid product data.");

            try
            {
                _logger.LogInformation("Creating new product: {Name}", productDto.Name);
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
                var createdProduct = response.Models.First();

                var resultDto = new ProductDto
                {
                    Id = createdProduct.Id,
                    ProductCode = createdProduct.ProductCode,
                    Name = createdProduct.Name,
                    Quantity = createdProduct.Quantity,
                    Price = createdProduct.Price,
                    PrixAchat = createdProduct.PrixAchat,
                    SupplierId = createdProduct.SupplierId,
                    CreatedAt = createdProduct.CreatedAt
                };

                _logger.LogInformation("Created product {Id} successfully.", createdProduct.Id);
                return Ok(resultDto);
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation creating product: {Message}", pe.Message);
                return StatusCode(403, "Permission denied: RLS violated.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error creating product: {Message}", pe.Message);
                return StatusCode(500, "Error creating product. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating product: {Message}", ex.Message);
                return StatusCode(500, "Error creating product. Check logs.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto)
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            if (id <= 0 || productDto == null || string.IsNullOrWhiteSpace(productDto.Name) || productDto.Quantity < 0)
                return BadRequest("Invalid product ID or data.");

            try
            {
                _logger.LogInformation("Updating product {Id}.", id);
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
                _logger.LogInformation("Updated product {Id} successfully.", id);
                return Ok();
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error updating product {Id}: {Message}", id, pe.Message);
                return StatusCode(500, $"Error updating product {id}. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating product {Id}: {Message}", id, ex.Message);
                return StatusCode(500, $"Error updating product {id}. Check logs.");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            if (id <= 0) return BadRequest("Invalid product ID.");

            try
            {
                _logger.LogInformation("Deleting product {Id}.", id);
                await _supabase.From<Product>().Where(p => p.Id == id).Delete();
                _logger.LogInformation("Deleted product {Id} successfully.", id);
                return Ok();
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error deleting product {Id}: {Message}", id, pe.Message);
                return StatusCode(500, $"Error deleting product {id}. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting product {Id}: {Message}", id, ex.Message);
                return StatusCode(500, $"Error deleting product {id}. Check logs.");
            }
        }

        [HttpGet("sales")]
        public async Task<IActionResult> GetProductSales()
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !await IsUserAdmin(userId))
            {
                return Forbid("Admin access required.");
            }

            try
            {
                _logger.LogInformation("Fetching product sales.");
                var salesResponse = await _supabase.From<Sale>().Get();
                var saleDtos = (salesResponse.Models ?? new List<Sale>())
                    .Select(s => new SaleDto
                    {
                        Id = s.Id,
                        ProductId = s.ProductId,
                        QuantitySold = s.QuantitySold,
                        SaleDate = s.SaleDate
                    }).ToList();

                _logger.LogInformation("Fetched {Count} sales successfully.", saleDtos.Count);
                return Ok(saleDtos);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error fetching sales: {Message}", pe.Message);
                return StatusCode(500, "Error fetching sales. Check database/logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching sales: {Message}", ex.Message);
                return StatusCode(500, "Error fetching sales. Check logs.");
            }
        }

        // ---------------- Helper ----------------
        private async Task<string> GenerateNextProductCode()
        {
            try
            {
                _logger.LogInformation("Generating next product code.");
                var products = await _supabase.From<Product>()
                    .Where(p => p.ProductCode != null && p.ProductCode.StartsWith("PR"))
                    .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                if (products.Models != null && products.Models.Any())
                {
                    var lastCode = products.Models.First().ProductCode;
                    if (lastCode != null && lastCode.Length > 2 && int.TryParse(lastCode.Substring(2), out var numberPart))
                    {
                        return $"PR{numberPart + 1:D3}";
                    }
                }

                return "PR001";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating next product code, defaulting to PR001: {Message}", ex.Message);
                return "PR001";
            }
        }
    }

    // Inline DTO for sales (updated to match Sale model properties)
    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }
}