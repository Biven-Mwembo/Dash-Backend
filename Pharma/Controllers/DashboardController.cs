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
        [Authorize(Roles = "admin")] // Dashboard data is usually sensitive admin info
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                // 1. Start tasks in parallel
                // Optimization: Only get sales from the last 30 days to calculate "Top Product"
                var salesTask = _supabase.From<Sale>()
                    .Where(s => s.SaleDate >= DateTime.UtcNow.AddDays(-30))
                    .Get();

                var lowStockTask = _supabase.From<Product>()
                    .Where(p => p.Quantity <= 5)
                    .Get();

                await Task.WhenAll(salesTask, lowStockTask);

                var sales = salesTask.Result.Models ?? new List<Sale>();
                Product? topProduct = null;

                // 2. Calculate Top Product ID from the list
                if (sales.Any())
                {
                    var topProductId = sales.GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    // Fetch the details for just that one top product
                    var pRes = await _supabase.From<Product>().Where(p => p.Id == topProductId).Single();
                    topProduct = pRes;
                }

                return Ok(new DashboardData
                {
                    MostSellingProduct = topProduct,
                    LowStockProducts = lowStockTask.Result.Models ?? new List<Product>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard Error");
                return StatusCode(500, new { message = "Dashboard failed to load", detail = ex.Message });
            }
        }
    }

    public class DashboardData
    {
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }
}