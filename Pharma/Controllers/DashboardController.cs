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
            try
            {
                var salesTask = _supabase.From<Sale>().Get();
                var lowStockTask = _supabase.From<Product>().Where(p => p.Quantity <= 5).Get();
                await Task.WhenAll(salesTask, lowStockTask);

                var sales = salesTask.Result.Models ?? new List<Sale>();
                Product? topProduct = null;

                if (sales.Any())
                {
                    var topProductId = sales.GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key).FirstOrDefault();

                    var pRes = await _supabase.From<Product>().Where(p => p.Id == topProductId).Get();
                    topProduct = pRes.Models.FirstOrDefault();
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
                return StatusCode(500, "Dashboard failed to load.");
            }
        }
    }

    public class DashboardData
    {
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }
}