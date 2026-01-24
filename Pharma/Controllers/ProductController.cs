using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using System.Security.Claims;
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
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId)) return false;
            try
            {
                var result = await _supabase.From<User>().Where(u => u.Id == userId).Get();
                return result.Models.FirstOrDefault()?.Role?.ToLower() == "admin";
            }
            catch { return false; }
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var response = await _supabase.From<Product>().Get();
            return Ok(response.Models ?? new List<Product>());
        }

        // ✅ FIX: Explicit route for /api/products/sales
        [HttpGet("sales")]
        public async Task<IActionResult> GetAllSales()
        {
            var response = await _supabase.From<Sale>().Get();
            var dtos = response.Models.Select(s => new SaleDto
            {
                Id = s.Id,
                ProductId = s.ProductId,
                QuantitySold = s.QuantitySold,
                SaleDate = s.SaleDate
            });
            return Ok(dtos);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductDto dto)
        {
            if (!await IsUserAdmin()) return Forbid();
            var product = new Product
            {
                ProductCode = string.IsNullOrEmpty(dto.ProductCode) ? await GenerateNextProductCode() : dto.ProductCode,
                Name = dto.Name,
                Quantity = dto.Quantity,
                Price = dto.Price,
                PrixAchat = dto.PrixAchat,
                SupplierId = dto.SupplierId
            };
            var res = await _supabase.From<Product>().Insert(product);
            return Ok(res.Models.First());
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto dto)
        {
            if (!await IsUserAdmin()) return Forbid();
            var update = new Product { Id = id, Name = dto.Name, Quantity = dto.Quantity, Price = dto.Price, PrixAchat = dto.PrixAchat, ProductCode = dto.ProductCode };
            await _supabase.From<Product>().Where(p => p.Id == id).Update(update);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(long id)
        {
            if (!await IsUserAdmin()) return Forbid();
            await _supabase.From<Product>().Where(p => p.Id == id).Delete();
            return Ok();
        }

        [HttpPost("sale")]
        public async Task<IActionResult> ProcessSale([FromBody] List<SaleItemDto> items)
        {
            if (items == null || !items.Any()) return BadRequest("Basket is empty.");
            try
            {
                foreach (var item in items)
                {
                    var product = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();
                    if (product == null || product.Quantity < item.QuantitySold) return BadRequest("Stock issue.");
                    product.Quantity -= item.QuantitySold;
                    await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);
                    await _supabase.From<Sale>().Insert(new Sale { ProductId = item.ProductId, QuantitySold = item.QuantitySold });
                }
                return Ok(new { message = "Vente réussie." });
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        private async Task<string> GenerateNextProductCode()
        {
            var res = await _supabase.From<Product>().Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending).Limit(1).Get();
            if (res.Models.Any())
            {
                var last = res.Models.First().ProductCode;
                if (int.TryParse(last?.Replace("PR", ""), out var n)) return $"PR{(n + 1):D3}";
            }
            return "PR001";
        }
    }
}