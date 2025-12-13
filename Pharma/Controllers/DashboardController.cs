using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pharma.Models;
using Supabase;
using Supabase.Postgrest.Exceptions;
using Product = Pharma.Models.Product;

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
            var dashboardData = new DashboardData
            {
                MostSellingProduct = null,
                LowStockProducts = new List<Product>()
            };

            try
            {
                // Fetch sales data
                var salesResponse = await _supabase.From<Sale>().Get();
                var sales = salesResponse.Models ?? new List<Sale>();

                if (sales.Any())
                {
                    var mostSellingProductId = sales
                        .GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    if (mostSellingProductId != 0)
                    {
                        try
                        {
                            dashboardData.MostSellingProduct = await _supabase
                                .From<Product>()
                                .Where(p => p.Id == mostSellingProductId)
                                .Single();
                        }
                        catch (PostgrestException pe)
                        {
                            _logger.LogWarning(pe, "Most selling product not found for ID {ProductId}", mostSellingProductId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sales data");
                // Continue to fetch low stock products even if sales fail
            }

            try
            {
                // Fetch low stock products
                var lowStockResponse = await _supabase
                    .From<Product>()
                    .Where(p => p.Quantity <= 5)
                    .Get();

                dashboardData.LowStockProducts = lowStockResponse.Models ?? new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving low stock products");
                // LowStockProducts remains empty list
            }

            return Ok(dashboardData);
        }
    }

    public class DashboardData
    {
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }
}
