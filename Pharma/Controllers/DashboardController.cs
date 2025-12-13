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

            // IMPROVEMENT: Run independent DB queries concurrently
            var salesTask = _supabase.From<Sale>().Get();
            var lowStockTask = _supabase
                .From<Product>()
                .Where(p => p.Quantity <= 5)
                .Get();

            try
            {
                // Wait for both tasks to complete simultaneously
                await Task.WhenAll(salesTask, lowStockTask);

                // --- 1. Process Sales Data for Most Selling Product ---
                var salesResponse = salesTask.Result;
                var sales = salesResponse.Models ?? new List<Sale>();

                if (sales.Any())
                {
                    // Find the ProductId with the highest total quantity sold
                    var mostSellingProductId = sales
                        .GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    if (mostSellingProductId != 0)
                    {
                        try
                        {
                            // Fetch the corresponding product using the ID
                            // FIX: Assuming .Single() returns the Product model directly
                            var product = await _supabase
                                .From<Product>()
                                .Where(p => p.Id == mostSellingProductId)
                                .Single();

                            dashboardData.MostSellingProduct = product;
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
                // Log and continue gracefully
                _logger.LogError(ex, "Error retrieving sales data for dashboard (MostSellingProduct might be null)");
            }

            try
            {
                // --- 2. Process Low Stock Products ---
                // Result is already ready from the Task.WhenAll call above
                var lowStockResponse = lowStockTask.Result;
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