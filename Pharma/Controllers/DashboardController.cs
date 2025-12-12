using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using Product = Pharma.Models.Product;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly Supabase.Client _supabase;

        public DashboardController(Supabase.Client supabase)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                // Fetch detailed sales for most selling product calculation
                var sales = await _supabase.From<Sale>().Get();
                var mostSellingProductId = sales.Models
                    .GroupBy(s => s.ProductId)
                    .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                    .Select(g => g.Key)
                    .FirstOrDefault();

                Product? mostSellingProduct = null;
                if (mostSellingProductId > 0)  // Check if valid long ID
                {
                    mostSellingProduct = await _supabase
                        .From<Product>()
                        .Where(p => p.Id == mostSellingProductId)  // Direct long comparison
                        .Single();
                }

                // Low stock: Products with quantity <= 5
                var lowStockProducts = (await _supabase
                    .From<Product>()
                    .Where(p => p.Quantity <= 5)
                    .Get())
                    .Models;

                return Ok(new DashboardData
                {
                    MostSellingProduct = mostSellingProduct,
                    LowStockProducts = lowStockProducts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving dashboard data: {ex.Message}");
            }
        }
    }
}
