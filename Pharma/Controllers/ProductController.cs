using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.Security.Claims;
using Supabase.Postgrest.Exceptions;

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

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user ID found in token claims");
                return false;
            }

            try
            {
                var result = await _supabase.From<User>()
                    .Where(u => u.Id == userId)
                    .Get();

                var userRecord = result.Models?.FirstOrDefault();

                if (userRecord == null)
                {
                    _logger.LogWarning("User {UserId} not found in database", userId);
                    return false;
                }

                return userRecord.Role?.ToLower() == "admin";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin check failed for User {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// GET: api/products - Retrieve all products
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                _logger.LogInformation("Fetching all products");

                var response = await _supabase.From<Product>()
                    .Order(p => p.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                var products = response.Models ?? new List<Product>();

                _logger.LogInformation("Successfully fetched {Count} products", products.Count);

                return Ok(products);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "Postgrest error fetching products: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error fetching products", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching products");
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
            }
        }

        /// <summary>
        /// GET: api/products/{id} - Get single product
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            try
            {
                _logger.LogInformation("Fetching product {ProductId}", id);

                var response = await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Single();

                if (response == null)
                {
                    _logger.LogWarning("Product {ProductId} not found", id);
                    return NotFound(new { message = $"Product with ID {id} not found" });
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product {ProductId}", id);
                return StatusCode(500, new { message = "Error fetching product", detail = ex.Message });
            }
        }

        /// <summary>
        /// GET: api/products/sales/daily - Get daily sales statistics
        /// </summary>
        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                _logger.LogInformation("Fetching daily sales data");

                var response = await _supabase.From<Sale>().Get();
                var sales = response.Models ?? new List<Sale>();

                var dailyTotals = sales
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new
                    {
                        Date = g.Key.ToString("yyyy-MM-dd"),
                        Count = g.Sum(s => s.QuantitySold),
                        TotalSales = g.Count()
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                _logger.LogInformation("Successfully compiled {Count} days of sales data", dailyTotals.Count);

                return Ok(dailyTotals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching daily sales");
                return StatusCode(500, new { message = "Error fetching sales data", detail = ex.Message });
            }
        }

        /// <summary>
        /// POST: api/products - Create new product (Admin only)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!await IsUserAdmin())
            {
                _logger.LogWarning("Non-admin user attempted to create product");
                return Forbid();
            }

            try
            {
                var productCode = string.IsNullOrEmpty(dto.ProductCode)
                    ? await GenerateNextProductCode()
                    : dto.ProductCode;

                var product = new Product
                {
                    ProductCode = productCode,
                    Name = dto.Name,
                    Quantity = dto.Quantity,
                    Price = dto.Price,
                    PrixAchat = dto.PrixAchat,
                    SupplierId = dto.SupplierId,
                    CreatedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Creating product with code {ProductCode}", productCode);

                var response = await _supabase.From<Product>().Insert(product);
                var createdProduct = response.Models?.FirstOrDefault();

                if (createdProduct == null)
                {
                    _logger.LogError("Product creation returned null");
                    return StatusCode(500, new { message = "Failed to create product" });
                }

                _logger.LogInformation("Successfully created product {ProductId}", createdProduct.Id);

                return CreatedAtAction(nameof(GetProduct), new { id = createdProduct.Id }, createdProduct);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "Database error creating product: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product");
                return StatusCode(500, new { message = "Error creating product", detail = ex.Message });
            }
        }

        /// <summary>
        /// PUT: api/products/{id} - Update existing product (Admin only)
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!await IsUserAdmin())
            {
                _logger.LogWarning("Non-admin user attempted to update product {ProductId}", id);
                return Forbid();
            }

            try
            {
                // Verify product exists
                var existingResponse = await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Get();

                if (existingResponse.Models == null || !existingResponse.Models.Any())
                {
                    _logger.LogWarning("Product {ProductId} not found for update", id);
                    return NotFound(new { message = $"Product with ID {id} not found" });
                }

                var product = new Product
                {
                    Id = id,
                    ProductCode = dto.ProductCode,
                    Name = dto.Name,
                    Quantity = dto.Quantity,
                    Price = dto.Price,
                    PrixAchat = dto.PrixAchat,
                    SupplierId = dto.SupplierId
                };

                _logger.LogInformation("Updating product {ProductId}", id);

                await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Update(product);

                _logger.LogInformation("Successfully updated product {ProductId}", id);

                return Ok(new { message = "Product updated successfully", productId = id });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "Database error updating product {ProductId}: {Message}", id, pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product {ProductId}", id);
                return StatusCode(500, new { message = "Error updating product", detail = ex.Message });
            }
        }

        /// <summary>
        /// DELETE: api/products/{id} - Delete product (Admin only)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            if (!await IsUserAdmin())
            {
                _logger.LogWarning("Non-admin user attempted to delete product {ProductId}", id);
                return Forbid();
            }

            try
            {
                // Check if product exists
                var existingResponse = await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Get();

                if (existingResponse.Models == null || !existingResponse.Models.Any())
                {
                    _logger.LogWarning("Product {ProductId} not found for deletion", id);
                    return NotFound(new { message = $"Product with ID {id} not found" });
                }

                _logger.LogInformation("Deleting product {ProductId}", id);

                await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Delete();

                _logger.LogInformation("Successfully deleted product {ProductId}", id);

                return Ok(new { message = "Product deleted successfully", productId = id });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "Database error deleting product {ProductId}: {Message}", id, pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product {ProductId}", id);
                return StatusCode(500, new { message = "Error deleting product", detail = ex.Message });
            }
        }

        /// <summary>
        /// POST: api/products/sale - Process sale transaction
        /// </summary>
        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            if (items == null || !items.Any())
            {
                return BadRequest(new { message = "Basket is empty" });
            }

            try
            {
                _logger.LogInformation("Processing sale with {ItemCount} items", items.Count);

                // Validate all products first
                foreach (var item in items)
                {
                    var productResponse = await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Get();

                    var product = productResponse.Models?.FirstOrDefault();

                    if (product == null)
                    {
                        _logger.LogWarning("Product {ProductId} not found during sale", item.ProductId);
                        return BadRequest(new { message = $"Product ID {item.ProductId} not found" });
                    }

                    if (product.Quantity < item.QuantitySold)
                    {
                        _logger.LogWarning("Insufficient stock for product {ProductId}. Available: {Available}, Requested: {Requested}",
                            item.ProductId, product.Quantity, item.QuantitySold);
                        return BadRequest(new
                        {
                            message = $"Insufficient stock for product '{product.Name}'",
                            productId = item.ProductId,
                            available = product.Quantity,
                            requested = item.QuantitySold
                        });
                    }
                }

                // Process each sale item
                foreach (var item in items)
                {
                    // Fetch current product state
                    var productResponse = await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Single();

                    // Update quantity
                    productResponse.Quantity -= item.QuantitySold;

                    await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Update(productResponse);

                    // Record sale
                    var sale = new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    };

                    await _supabase.From<Sale>().Insert(sale);

                    _logger.LogInformation("Processed sale for product {ProductId}, quantity {Quantity}",
                        item.ProductId, item.QuantitySold);
                }

                _logger.LogInformation("Sale completed successfully");

                return Ok(new
                {
                    success = true,
                    message = "Vente réussie",
                    itemsProcessed = items.Count
                });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "Database error processing sale: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error during sale", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sale");
                return StatusCode(500, new { message = "Error processing sale", detail = ex.Message });
            }
        }

        private async Task<string> GenerateNextProductCode()
        {
            try
            {
                var response = await _supabase.From<Product>()
                    .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                if (response.Models != null && response.Models.Any())
                {
                    var lastCode = response.Models.First().ProductCode;
                    if (!string.IsNullOrEmpty(lastCode) && lastCode.StartsWith("PR"))
                    {
                        if (int.TryParse(lastCode.Replace("PR", ""), out var num))
                        {
                            return $"PR{(num + 1):D3}";
                        }
                    }
                }

                _logger.LogInformation("No existing product codes found, starting with PR001");
                return "PR001";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating product code, defaulting to PR001");
                return "PR001";
            }
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