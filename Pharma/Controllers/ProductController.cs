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
                        CreatedAt = p.CreatedAt
                    }).ToList();

                return Ok(productDtos);
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error fetching products");
                return StatusCode(500, "Error fetching products. Check logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching products");
                return StatusCode(500, "Error fetching products. Check logs.");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            try
            {
                Product product = null;
                try
                {
                    product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
                }
                catch (PostgrestException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                {
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

                return Ok(productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving product {Id}", id);
                return StatusCode(500, $"Error retrieving product {id}. Check logs.");
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
                    CreatedAt = createdProduct.CreatedAt
                };

                return Ok(resultDto);
            }
            catch (PostgrestException pe) when (pe.Message.Contains("violates row-level security policy"))
            {
                _logger.LogWarning(pe, "RLS policy violation creating product");
                return StatusCode(403, "Permission denied: RLS violated.");
            }
            catch (PostgrestException pe)
            {
                _logger.LogError(pe, "Postgrest error creating product");
                return StatusCode(500, "Error creating product. Check logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating product");
                return StatusCode(500, "Error creating product. Check logs.");
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
                return StatusCode(500, $"Error updating product {id}. Check logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating product {Id}", id);
                return StatusCode(500, $"Error updating product {id}. Check logs.");
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
                return StatusCode(500, $"Error deleting product {id}. Check logs.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting product {Id}", id);
                return StatusCode(500, $"Error deleting product {id}. Check logs.");
            }
        }

        // ---------------- Helper ----------------
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
