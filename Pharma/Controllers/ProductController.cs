using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pharma.Models;
using Supabase;
using Supabase.Postgrest.Exceptions;
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;

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

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
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
                        // FIX: CreatedAt is non-nullable DateTime, use directly
                        CreatedAt = p.CreatedAt
                    }).ToList();

                return Ok(productDtos);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error fetching products");
                return StatusCode(500, "Error fetching products. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching products");
                return StatusCode(500, "Error fetching products. Please check logs for details.");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            try
            {
                // FIX: Assuming .Single() returns the Product model directly (as indicated by your error)
                var product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
                if (product == null) return NotFound();

                var productDto = new ProductDto
                {
                    Id = product.Id,
                    ProductCode = product.ProductCode,
                    Name = product.Name,
                    Quantity = product.Quantity,
                    Price = product.Price,
                    PrixAchat = product.PrixAchat,
                    SupplierId = product.SupplierId,
                    // FIX: CreatedAt is non-nullable DateTime, use directly
                    CreatedAt = product.CreatedAt
                };
                return Ok(productDto);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error retrieving product {Id}", id);
                return StatusCode(500, $"Error retrieving product {id}. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving product {Id}", id);
                return StatusCode(500, $"Error retrieving product {id}. Please check logs for details.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)
        {
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
                    // FIX: CreatedAt is non-nullable DateTime, use directly
                    CreatedAt = createdProduct.CreatedAt
                };

                return Ok(resultDto);
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation creating product");
                return StatusCode(403, "Permission Denied: RLS policy violated");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error creating product");
                return StatusCode(500, "Error creating product. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating product");
                return StatusCode(500, "Error creating product. Please check logs for details.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto)
        {
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
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error updating product {Id}", id);
                return StatusCode(500, $"Error updating product {id}. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating product {Id}", id);
                return StatusCode(500, $"Error updating product {id}. Please check logs for details.");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            try
            {
                await _supabase.From<Product>().Where(p => p.Id == id).Delete();
                return Ok();
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error deleting product {Id}", id);
                return StatusCode(500, $"Error deleting product {id}. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting product {Id}", id);
                return StatusCode(500, $"Error deleting product {id}. Please check logs for details.");
            }
        }

        [HttpPost("{id}/sell")]
        public async Task<IActionResult> SellProduct(long id, [FromBody] int quantitySold)
        {
            try
            {
                // FIX: Assuming .Single() returns the Product model directly
                var product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
                if (product == null) return NotFound("Product not found.");
                if (product.Quantity < quantitySold) return BadRequest("Insufficient stock.");

                product.Quantity -= quantitySold;
                await _supabase.From<Product>().Where(p => p.Id == id).Update(product);
                await _supabase.From<Sale>().Insert(new Sale { ProductId = id, QuantitySold = quantitySold });

                return Ok($"Sold {quantitySold} units of {product.Name}. Remaining quantity: {product.Quantity}");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error selling product {Id}", id);
                return StatusCode(500, $"Error selling product {id}. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error selling product {Id}", id);
                return StatusCode(500, $"Error selling product {id}. Please check logs for details.");
            }
        }

        [HttpPost("sale")]
        public async Task<IActionResult> CreateSale([FromBody] List<SaleItemDto> saleItems)
        {
            try
            {
                foreach (var item in saleItems)
                {
                    // FIX: Assuming .Single() returns the Product model directly
                    var product = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();
                    if (product == null) return BadRequest($"Product {item.ProductId} not found.");
                    if (product.Quantity < item.QuantitySold) return BadRequest($"Insufficient stock for {product.Name}.");

                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);
                    await _supabase.From<Sale>().Insert(new Sale { ProductId = item.ProductId, QuantitySold = item.QuantitySold });
                }

                return Ok("Sale completed successfully.");
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation processing sale");
                return StatusCode(403, "Permission Denied: RLS policy violated for products or sales.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error processing sale");
                return StatusCode(500, "Error processing sale. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing sale");
                return StatusCode(500, "Error processing sale. Please check logs for details.");
            }
        }

        [HttpGet("sales")]
        public async Task<IActionResult> GetSales()
        {
            try
            {
                // IMPROVEMENT: Run DB queries concurrently to speed up response time (Task.WhenAll)
                var salesTask = _supabase.From<Sale>().Get();
                var productsTask = _supabase.From<Product>().Get();

                await Task.WhenAll(salesTask, productsTask);

                var sales = salesTask.Result;
                var products = productsTask.Result;

                var productList = products.Models ?? new List<Product>();
                var salesList = sales.Models ?? new List<Sale>();

                var salesWithDetails = salesList.Select(s =>
                {
                    var product = productList.FirstOrDefault(p => p.Id == s.ProductId);
                    if (product == null) return null;

                    return new
                    {
                        ProductName = product.Name,
                        QuantitySold = s.QuantitySold,
                        PricePerItem = product.Price,
                        TotalAmount = product.Price * s.QuantitySold,
                        // FIX: SaleDate is non-nullable DateTime, use directly
                        SaleDate = s.SaleDate
                    };
                }).Where(s => s != null).ToList();

                return Ok(salesWithDetails);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error retrieving sales");
                return StatusCode(500, "Error retrieving sales. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving sales");
                return StatusCode(500, "Error retrieving sales. Please check logs for details.");
            }
        }

        [HttpGet("sales/daily")]
        public async Task<IActionResult> GetDailySales()
        {
            try
            {
                // IMPROVEMENT: Run DB queries concurrently to speed up response time (Task.WhenAll)
                var salesTask = _supabase.From<Sale>().Get();
                var productsTask = _supabase.From<Product>().Get();

                await Task.WhenAll(salesTask, productsTask);

                var sales = salesTask.Result;
                var products = productsTask.Result;

                var productList = products.Models ?? new List<Product>();
                var salesList = sales.Models ?? new List<Sale>();

                var dailySales = salesList
                    // FIX: SaleDate is non-nullable DateTime, use .Date directly
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g =>
                    {
                        var totalAmount = g.Sum(s =>
                        {
                            var product = productList.FirstOrDefault(p => p.Id == s.ProductId);
                            return product != null ? product.Price * s.QuantitySold : 0.0m;
                        });

                        return new
                        {
                            Date = g.Key,
                            Day = g.Key.ToString("dddd"),
                            NumberOfSales = g.Count(),
                            TotalAmount = totalAmount
                        };
                    })
                    .OrderByDescending(d => d.Date)
                    .ToList();

                return Ok(dailySales);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error retrieving daily sales");
                return StatusCode(500, "Error retrieving daily sales. Please check logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving daily sales");
                return StatusCode(500, "Error retrieving daily sales. Please check logs for details.");
            }
        }

        private async Task<string> GenerateNextProductCode()
        {
            try
            {
                var products = await _supabase.From<Product>()
                    .Where(p => p.ProductCode != null && p.ProductCode.StartsWith("PR"))
                    .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                if (products.Models != null && products.Models.Any())
                {
                    var lastCode = products.Models.First().ProductCode;
                    // IMPROVEMENT: Added null check for lastCode before Substring/TryParse
                    if (lastCode != null && lastCode.Length > 2 && int.TryParse(lastCode.Substring(2), out var numberPart))
                    {
                        return $"PR{numberPart + 1:D3}";
                    }
                }

                return "PR001";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating next product code, defaulting to PR001");
                return "PR001";
            }
        }
    }
}