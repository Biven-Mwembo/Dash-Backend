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
    [Authorize] // Requires a valid Supabase login token
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
                _logger.LogInformation("Fetching dashboard data for authenticated user.");

                // Run both queries in parallel for better performance
                var salesTask = _supabase.From<Sale>().Get();
                var lowStockTask = _supabase.From<Product>().Where(p => p.Quantity <= 5).Get();

                await Task.WhenAll(salesTask, lowStockTask);

                // 1. Process Most Selling Product
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
                            // Fetch the actual product details for the top seller
                            var product = await _supabase.From<Product>()
                                .Where(p => p.Id == mostSellingProductId)
                                .Single();

                            dashboardData.MostSellingProduct = product;
                        }
                        catch (PostgrestException pe) when (pe.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning("Top product ID {Id} not found in products table.", mostSellingProductId);
                        }
                    }
                }

                // 2. Process Low Stock Products
                var lowStockResponse = lowStockTask.Result;
                dashboardData.LowStockProducts = lowStockResponse.Models ?? new List<Product>();

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving dashboard data.");
                return StatusCode(500, "Internal server error fetching dashboard stats.");
            }
        }
    }

    public class DashboardData
    {
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }
}