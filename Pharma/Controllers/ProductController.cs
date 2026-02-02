using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;

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
            _supabase = supabase;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                _logger.LogInformation("Fetching all products");

                var response = await _supabase.From<Product>().Get();
                var products = response?.Models ?? new List<Product>();

                _logger.LogInformation("Found {Count} products", products.Count);

                // ✅ Map to DTOs to avoid serialization issues
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
                _logger.LogError(ex, "Error fetching products: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    Message = "Error fetching products",
                    Detail = ex.Message
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            try
            {
                _logger.LogInformation("Fetching product: {Id}", id);

                var product = await _supabase
                    .From<Product>()
                    .Where(p => p.Id == id)
                    .Single();

                if (product == null)
                {
                    _logger.LogWarning("Product not found: {Id}", id);
                    return NotFound(new { Message = "Product not found" });
                }

                // ✅ Map to DTO
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

                return Ok(productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product {Id}", id);
                return StatusCode(500, new
                {
                    Message = "Error fetching product",
                    Detail = ex.Message
                });
            }
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto dto)
        {
            try
            {
                _logger.LogInformation("Creating product: {Name}", dto.Name);

                var product = new Product
                {
                    ProductCode = dto.ProductCode ?? $"PR{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                    Name = dto.Name,
                    Quantity = dto.Quantity,
                    Price = dto.Price,
                    PrixAchat = dto.PrixAchat,
                    SupplierId = dto.SupplierId,
                    CreatedAt = DateTime.UtcNow
                };

                var response = await _supabase.From<Product>().Insert(product);
                var created = response?.Models?.FirstOrDefault();

                if (created == null)
                {
                    _logger.LogError("Product creation returned null");
                    return StatusCode(500, new { Message = "Failed to create product" });
                }

                _logger.LogInformation("Product created: {Id}", created.Id);

                // ✅ Map to DTO
                var productDto = new ProductDto
                {
                    Id = created.Id,
                    ProductCode = created.ProductCode,
                    Name = created.Name,
                    Quantity = created.Quantity,
                    Price = created.Price,
                    PrixAchat = created.PrixAchat,
                    SupplierId = created.SupplierId,
                    CreatedAt = created.CreatedAt
                };

                return CreatedAtAction(nameof(GetProduct), new { id = productDto.Id }, productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product");
                return StatusCode(500, new
                {
                    Message = "Error creating product",
                    Detail = ex.Message
                });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] UpdateProductDto dto)
        {
            try
            {
                _logger.LogInformation("Updating product: {Id}", id);

                var existing = await _supabase
                    .From<Product>()
                    .Where(p => p.Id == id)
                    .Single();

                if (existing == null)
                {
                    return NotFound(new { Message = "Product not found" });
                }

                existing.ProductCode = dto.ProductCode ?? existing.ProductCode;
                existing.Name = dto.Name;
                existing.Quantity = dto.Quantity;
                existing.Price = dto.Price;
                existing.PrixAchat = dto.PrixAchat;
                existing.SupplierId = dto.SupplierId;

                await _supabase.From<Product>().Update(existing);

                _logger.LogInformation("Product updated: {Id}", id);

                // ✅ Map to DTO
                var productDto = new ProductDto
                {
                    Id = existing.Id,
                    ProductCode = existing.ProductCode,
                    Name = existing.Name,
                    Quantity = existing.Quantity,
                    Price = existing.Price,
                    PrixAchat = existing.PrixAchat,
                    SupplierId = existing.SupplierId,
                    CreatedAt = existing.CreatedAt
                };

                return Ok(productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product {Id}", id);
                return StatusCode(500, new
                {
                    Message = "Error updating product",
                    Detail = ex.Message
                });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            try
            {
                _logger.LogInformation("Deleting product: {Id}", id);

                await _supabase
                    .From<Product>()
                    .Where(p => p.Id == id)
                    .Delete();

                _logger.LogInformation("Product deleted: {Id}", id);

                return Ok(new { Message = "Product deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product {Id}", id);
                return StatusCode(500, new
                {
                    Message = "Error deleting product",
                    Detail = ex.Message
                });
            }
        }

        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            try
            {
                _logger.LogInformation("Processing sale with {Count} items", items?.Count ?? 0);

                if (items == null || !items.Any())
                {
                    return BadRequest(new { Message = "No items provided" });
                }

                foreach (var item in items)
                {
                    var product = await _supabase
                        .From<Product>()
                        .Where(p => p.Id == item.ProductId)
                        .Single();

                    if (product == null)
                    {
                        return BadRequest(new
                        {
                            Message = $"Product {item.ProductId} not found"
                        });
                    }

                    if (product.Quantity < item.QuantitySold)
                    {
                        return BadRequest(new
                        {
                            Message = $"Insufficient stock for {product.Name}"
                        });
                    }

                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Update(product);

                    var sale = new Sale
                    {
                        ProductId = item.ProductId,
                        QuantitySold = item.QuantitySold,
                        SaleDate = DateTime.UtcNow
                    };
                    await _supabase.From<Sale>().Insert(sale);
                }

                return Ok(new { Success = true, Message = "Sale processed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sale");
                return StatusCode(500, new
                {
                    Message = "Error processing sale",
                    Detail = ex.Message
                });
            }
        }

        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                _logger.LogInformation("Fetching daily sales");

                var today = DateTime.UtcNow.Date;
                var tomorrow = today.AddDays(1);

                var response = await _supabase
                    .From<Sale>()
                    .Where(s => s.SaleDate >= today && s.SaleDate < tomorrow)
                    .Get();

                var sales = response?.Models ?? new List<Sale>();

                // ✅ Map to DTOs
                var saleDtos = sales.Select(s => new SaleDto
                {
                    Id = s.Id,
                    ProductId = s.ProductId,
                    QuantitySold = s.QuantitySold,
                    SaleDate = s.SaleDate
                }).ToList();

                return Ok(new
                {
                    Date = today,
                    TotalQuantitySold = sales.Sum(s => s.QuantitySold),
                    SalesCount = sales.Count,
                    Sales = saleDtos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching daily sales");
                return StatusCode(500, new
                {
                    Message = "Error fetching daily sales",
                    Detail = ex.Message
                });
            }
        }
    }

    // ✅ Add these DTOs
    public class CreateProductDto
    {
        public string? ProductCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal PrixAchat { get; set; }
        public string? SupplierId { get; set; }
    }

    public class UpdateProductDto
    {
        public string? ProductCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal PrixAchat { get; set; }
        public string? SupplierId { get; set; }
    }
}