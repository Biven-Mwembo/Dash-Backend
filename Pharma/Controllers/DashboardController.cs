using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;

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
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                _logger.LogInformation("Fetching dashboard data");

                // Fetch sales from last 30 days
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

                var salesTask = _supabase
                    .From<Sale>()
                    .Where(s => s.SaleDate >= thirtyDaysAgo)
                    .Get();

                var lowStockTask = _supabase
                    .From<Product>()
                    .Where(p => p.Quantity <= 5)
                    .Get();

                await Task.WhenAll(salesTask, lowStockTask);

                var sales = salesTask.Result?.Models ?? new List<Sale>();
                var lowStockProducts = lowStockTask.Result?.Models ?? new List<Product>();

                Product? topProduct = null;

                if (sales.Any())
                {
                    var topProductId = sales
                        .GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    if (topProductId > 0)
                    {
                        topProduct = await _supabase
                            .From<Product>()
                            .Where(p => p.Id == topProductId)
                            .Single();
                    }
                }

                _logger.LogInformation("Dashboard data fetched successfully. Low stock items: {Count}", lowStockProducts.Count);

                return Ok(new
                {
                    MostSellingProduct = topProduct,
                    LowStockProducts = lowStockProducts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard error: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    Message = "Dashboard failed to load",
                    Detail = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }
    }
}