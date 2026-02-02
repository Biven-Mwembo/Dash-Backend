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
    [Authorize] // Globally requires a valid token for this controller
    public class ProductsController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(Supabase.Client supabase, ILogger<ProductsController> logger)
        {
            _supabase = supabase;
            _logger = logger;
        }

        // GET: api/products (All users)
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var response = await _supabase.From<Product>().Get();
            return Ok(response.Models ?? new List<Product>());
        }

        // GET: api/products/{id} (All users)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(long id)
        {
            var response = await _supabase.From<Product>().Where(p => p.Id == id).Single();
            if (response == null) return NotFound();
            return Ok(response);
        }

        // POST: api/products (Admin Only)
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto dto)
        {
            var product = new Product
            {
                ProductCode = dto.ProductCode ?? $"PR{Guid.NewGuid().ToString().Substring(0, 4)}",
                Name = dto.Name,
                Quantity = dto.Quantity,
                Price = dto.Price,
                PrixAchat = dto.PrixAchat,
                SupplierId = dto.SupplierId,
                CreatedAt = DateTime.UtcNow
            };

            var response = await _supabase.From<Product>().Insert(product);
            return CreatedAtAction(nameof(GetProduct), new { id = response.Models[0].Id }, response.Models[0]);
        }

        // DELETE: api/products/{id} (Admin Only)
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            await _supabase.From<Product>().Where(p => p.Id == id).Delete();
            return Ok(new { message = "Deleted successfully" });
        }

        // POST: api/products/sale (All authenticated users)
        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            foreach (var item in items)
            {
                var pRes = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();
                if (pRes == null || pRes.Quantity < item.QuantitySold) return BadRequest("Insufficient stock");

                pRes.Quantity -= item.QuantitySold;
                await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(pRes);
                await _supabase.From<Sale>().Insert(new Sale { ProductId = item.ProductId, QuantitySold = item.QuantitySold });
            }
            return Ok(new { success = true });
        }
    }
}