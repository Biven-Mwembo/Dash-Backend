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

            var salesTask = _supabase.From<Sale>().Get();
            var lowStockTask = _supabase.From<Product>().Where(p => p.Quantity <= 5).Get();

            try
            {
                await Task.WhenAll(salesTask, lowStockTask);

                // Most Selling Product
                var salesResponse = salesTask.Result;
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
                            var product = await _supabase.From<Product>()
                                .Where(p => p.Id == mostSellingProductId)
                                .Single();

                            dashboardData.MostSellingProduct = product;
                        }
                        catch (PostgrestException pe) when (pe.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning(pe, "Most selling product not found for ID {ProductId}", mostSellingProductId);
                        }
                        catch (PostgrestException pe)
                        {
                            _logger.LogError(pe, "Postgrest error fetching most selling product");
                        }
                    }
                }

                // Low Stock Products
                var lowStockResponse = lowStockTask.Result;
                dashboardData.LowStockProducts = lowStockResponse.Models ?? new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving dashboard data");
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
