using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(Supabase.Client supabase, ILogger<DashboardController> logger)
        {
            _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            var dashboardData = new DashboardData();

            try
            {
                _logger.LogInformation("Compiling dashboard statistics.");

                // Parallel fetching
                var salesTask = _supabase.From<Sale>().Get();
                var lowStockTask = _supabase.From<Product>().Where(p => p.Quantity <= 5).Get();
                await Task.WhenAll(salesTask, lowStockTask);

                // 1. Determine Most Selling Product
                var sales = salesTask.Result.Models ?? new List<Sale>();
                if (sales.Any())
                {
                    var topProductId = sales
                        .GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    if (topProductId != 0)
                    {
                        var productResponse = await _supabase.From<Product>()
                            .Where(p => p.Id == topProductId)
                            .Get();
                        dashboardData.MostSellingProduct = productResponse.Models.FirstOrDefault();
                    }
                }

                // 2. Map Low Stock
                dashboardData.LowStockProducts = lowStockTask.Result.Models ?? new List<Product>();

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard compilation failed.");
                return StatusCode(500, "Internal error.");
            }
        }
    }

    public class DashboardData
    {
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }
}