using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.Security.Claims;
using Supabase.Postgrest.Exceptions;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/products")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ProductsController> _logger;
        private readonly IConfiguration _configuration;

        public ProductsController(Supabase.Client supabase, ILogger<ProductsController> logger, IConfiguration configuration)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Extract user ID from JWT token
        /// </summary>
        private string? GetCurrentUserId()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("user_id")?.Value
                          ?? User.FindFirst("sub")?.Value;

                return userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting user ID from token");
                return null;
            }
        }

        /// <summary>
        /// Check if current user has admin role
        /// </summary>
        private bool IsUserAdmin()
        {
            try
            {
                var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
                             ?? User.FindFirst("role")?.Value;

                var userId = GetCurrentUserId();

                _logger.LogDebug("Checking admin status for user {UserId}. Role claim: {Role}",
                    userId ?? "unknown", roleClaim ?? "null");

                return roleClaim?.ToLower() == "admin";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking admin status");
                return false;
            }
        }

        /// <summary>
        /// Check if user is authenticated (any role)
        /// </summary>
        private bool IsAuthenticated()
        {
            try
            {
                var userId = GetCurrentUserId();
                var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;

                _logger.LogDebug("Authentication check. User ID: {UserId}, IsAuthenticated: {IsAuthenticated}",
                    userId ?? "unknown", isAuthenticated);

                return isAuthenticated && !string.IsNullOrEmpty(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking authentication status");
                return false;
            }
        }

        /// <summary>
        /// GET: api/products - Retrieve all products (All authenticated users)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                if (!IsAuthenticated())
                {
                    _logger.LogWarning("Unauthenticated user attempted to access products");
                    return Unauthorized(new { message = "Authentication required" });
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("User {UserId} fetching all products", userId);

                var response = await _supabase.From<Product>()
                    .Order(p => p.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                var products = response.Models ?? new List<Product>();

                _logger.LogInformation("Successfully fetched {Count} products for user {UserId}",
                    products.Count, userId);

                return Ok(products);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error fetching products: {Message}", pex.Message);
           

                return StatusCode(500, new
                {
                    message = "Database error fetching products",
                    detail = pex.Message,
                    hint = "Check RLS policies on products table in Supabase"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching products");
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
            }
        }

        /// <summary>
        /// GET: api/products/{id} - Get single product (All authenticated users)
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            try
            {
                if (!IsAuthenticated())
                {
                    _logger.LogWarning("Unauthenticated user attempted to access product {ProductId}", id);
                    return Unauthorized(new { message = "Authentication required" });
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("User {UserId} fetching product {ProductId}", userId, id);

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
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error fetching product {ProductId}: {Message}", id, pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product {ProductId}", id);
                return StatusCode(500, new { message = "Error fetching product", detail = ex.Message });
            }
        }

        /// <summary>
        /// GET: api/products/sales - Get all sales (Admin only)
        /// </summary>
        [HttpGet("sales")]
        public async Task<IActionResult> GetAllSales()
        {
            try
            {
                if (!IsUserAdmin())
                {
                    _logger.LogWarning("Non-admin user attempted to access all sales");
                    return Forbid();
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("Admin {UserId} fetching all sales", userId);

                var salesResponse = await _supabase.From<Sale>().Get();
                var sales = salesResponse.Models ?? new List<Sale>();

                var productsResponse = await _supabase.From<Product>().Get();
                var products = productsResponse.Models ?? new List<Product>();

                // Map sales with product information
                var salesWithDetails = sales.Select(sale => {
                    var product = products.FirstOrDefault(p => p.Id == sale.ProductId);
                    return new
                    {
                        id = sale.Id,
                        productId = sale.ProductId,
                        productName = product?.Name ?? $"Product #{sale.ProductId}",
                        quantitySold = sale.QuantitySold,
                        pricePerItem = product?.Price ?? 0,
                        totalAmount = sale.QuantitySold * (product?.Price ?? 0),
                        saleDate = sale.SaleDate
                    };
                }).OrderByDescending(s => s.saleDate);

                _logger.LogInformation("Successfully fetched {Count} sales for admin {UserId}",
                    salesWithDetails.Count(), userId);

                return Ok(salesWithDetails);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error fetching sales: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error fetching sales", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all sales");
                return StatusCode(500, new { message = "Error fetching sales", detail = ex.Message });
            }
        }

        /// <summary>
        /// GET: api/products/sales/daily - Get daily sales statistics (All authenticated users)
        /// </summary>
        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                if (!IsAuthenticated())
                {
                    _logger.LogWarning("Unauthenticated user attempted to access daily sales");
                    return Unauthorized(new { message = "Authentication required" });
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("User {UserId} fetching daily sales data", userId);

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

                _logger.LogInformation("Successfully compiled {Count} days of sales data for user {UserId}",
                    dailyTotals.Count, userId);

                return Ok(dailyTotals);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error fetching daily sales: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
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
                _logger.LogWarning("Invalid model state for product creation");
                return BadRequest(ModelState);
            }

            if (!IsUserAdmin())
            {
                var userId = GetCurrentUserId();
                _logger.LogWarning("Non-admin user {UserId} attempted to create product", userId);
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

                var userId = GetCurrentUserId();
                _logger.LogInformation("Admin {UserId} creating product with code {ProductCode}",
                    userId, productCode);

                var response = await _supabase.From<Product>().Insert(product);
                var createdProduct = response.Models?.FirstOrDefault();

                if (createdProduct == null)
                {
                    _logger.LogError("Product creation returned null for admin {UserId}", userId);
                    return StatusCode(500, new { message = "Failed to create product" });
                }

                _logger.LogInformation("Successfully created product {ProductId} by admin {UserId}",
                    createdProduct.Id, userId);

                return CreatedAtAction(nameof(GetProduct), new { id = createdProduct.Id }, createdProduct);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error creating product: {Message}", pex.Message);
                

                // Check for unique constraint violation
                if (pex.Message.Contains("23505") || pex.Message.ToLower().Contains("unique"))
                {
                    return BadRequest(new { message = "Product with this code already exists", detail = pex.Message });
                }

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

            if (!IsUserAdmin())
            {
                var userId = GetCurrentUserId();
                _logger.LogWarning("Non-admin user {UserId} attempted to update product {ProductId}",
                    userId, id);
                return Forbid();
            }

            try
            {
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

                var userId = GetCurrentUserId();
                _logger.LogInformation("Admin {UserId} updating product {ProductId}", userId, id);

                await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Update(product);

                _logger.LogInformation("Successfully updated product {ProductId} by admin {UserId}",
                    id, userId);

                return Ok(new { message = "Product updated successfully", productId = id });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error updating product {ProductId}: {Message}", id, pex.Message);
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
            if (!IsUserAdmin())
            {
                var userId = GetCurrentUserId();
                _logger.LogWarning("Non-admin user {UserId} attempted to delete product {ProductId}",
                    userId, id);
                return Forbid();
            }

            try
            {
                var existingResponse = await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Get();

                if (existingResponse.Models == null || !existingResponse.Models.Any())
                {
                    _logger.LogWarning("Product {ProductId} not found for deletion", id);
                    return NotFound(new { message = $"Product with ID {id} not found" });
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("Admin {UserId} deleting product {ProductId}", userId, id);

                await _supabase.From<Product>()
                    .Where(p => p.Id == id)
                    .Delete();

                _logger.LogInformation("Successfully deleted product {ProductId} by admin {UserId}",
                    id, userId);

                return Ok(new { message = "Product deleted successfully", productId = id });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error deleting product {ProductId}: {Message}", id, pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product {ProductId}", id);
                return StatusCode(500, new { message = "Error deleting product", detail = ex.Message });
            }
        }

        /// <summary>
        /// POST: api/products/sale - Process sale transaction (All authenticated users)
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
                if (!IsAuthenticated())
                {
                    _logger.LogWarning("Unauthenticated user attempted to process sale");
                    return Unauthorized(new { message = "Authentication required" });
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("User {UserId} processing sale with {ItemCount} items",
                    userId, items.Count);

                // Validate all products first
                foreach (var item in items)
                {
                    var productResponse = await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Get();

                    var product = productResponse.Models?.FirstOrDefault();

                    if (product == null)
                    {
                        _logger.LogWarning("Product {ProductId} not found during sale by user {UserId}",
                            item.ProductId, userId);
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
                    var productResponse = await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Single();

                    productResponse.Quantity -= item.QuantitySold;

                    await _supabase.From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Update(productResponse);

                    var sale = new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    };

                    await _supabase.From<Sale>().Insert(sale);

                    _logger.LogInformation("Processed sale for product {ProductId}, quantity {Quantity} by user {UserId}",
                        item.ProductId, item.QuantitySold, userId);
                }

                _logger.LogInformation("Sale completed successfully for user {UserId} with {ItemCount} items",
                    userId, items.Count);

                return Ok(new
                {
                    success = true,
                    message = "Vente réussie",
                    itemsProcessed = items.Count,
                    processedAt = DateTime.UtcNow
                });
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error processing sale: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error during sale", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sale");
                return StatusCode(500, new { message = "Error processing sale", detail = ex.Message });
            }
        }

        /// <summary>
        /// Generate next product code (PR001, PR002, etc.)
        /// </summary>
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
                            var newCode = $"PR{(num + 1):D3}";
                            _logger.LogInformation("Generated next product code: {NewCode} (last was {LastCode})",
                                newCode, lastCode);
                            return newCode;
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

        /// <summary>
        /// GET: api/products/stats - Get product statistics (Admin only)
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetProductStats()
        {
            try
            {
                if (!IsUserAdmin())
                {
                    _logger.LogWarning("Non-admin user attempted to access product stats");
                    return Forbid();
                }

                var userId = GetCurrentUserId();
                _logger.LogInformation("Admin {UserId} fetching product statistics", userId);

                var productsResponse = await _supabase.From<Product>().Get();
                var products = productsResponse.Models ?? new List<Product>();

                var stats = new
                {
                    TotalProducts = products.Count,
                    TotalQuantity = products.Sum(p => p.Quantity),
                    TotalInventoryValue = products.Sum(p => p.Quantity * p.Price),
                    LowStockProducts = products.Where(p => p.Quantity < 10).Count(),
                    OutOfStockProducts = products.Where(p => p.Quantity == 0).Count()
                };

                _logger.LogInformation("Successfully fetched product statistics for admin {UserId}", userId);

                return Ok(stats);
            }
            catch (PostgrestException pex)
            {
                _logger.LogError(pex, "PostgreSQL error fetching product stats: {Message}", pex.Message);
                return StatusCode(500, new { message = "Database error", detail = pex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product statistics");
                return StatusCode(500, new { message = "Error fetching statistics", detail = ex.Message });
            }
        }
    }

    /// <summary>
    /// DTO for creating/updating products
    /// </summary>
    public class ProductDto
    {
        public string? ProductCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal PrixAchat { get; set; }
        public string? SupplierId { get; set; }
    }

    /// <summary>
    /// DTO for sale items
    /// </summary>
    public class SaleItemDto
    {
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
    }

    /// <summary>
    /// DTO for sale response
    /// </summary>
    public class SaleResponseDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public string? ProductName { get; set; }
        public int QuantitySold { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime SaleDate { get; set; }
    }
}