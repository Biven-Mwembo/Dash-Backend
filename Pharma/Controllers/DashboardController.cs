using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pharma.Models;
using Supabase;

namespace Pharma.Controllers
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize(Roles = "admin")]
    public class DashboardController : ControllerBase
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(Supabase.Client supabase, ILogger<DashboardController> logger)
        {
            _supabase = supabase;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                _logger.LogInformation("Fetching dashboard data");

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

                ProductDto? topProductDto = null;

                if (sales.Any())
                {
                    var topProductId = sales
                        .GroupBy(s => s.ProductId)
                        .OrderByDescending(g => g.Sum(s => s.QuantitySold))
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    if (topProductId > 0)
                    {
                        var topProduct = await _supabase
                            .From<Product>()
                            .Where(p => p.Id == topProductId)
                            .Single();

                        if (topProduct != null)
                        {
                            topProductDto = new ProductDto
                            {
                                Id = topProduct.Id,
                                ProductCode = topProduct.ProductCode,
                                Name = topProduct.Name,
                                Quantity = topProduct.Quantity,
                                Price = topProduct.Price,
                                PrixAchat = topProduct.PrixAchat,
                                SupplierId = topProduct.SupplierId,
                                CreatedAt = topProduct.CreatedAt
                            };
                        }
                    }
                }

                var lowStockDtos = lowStockProducts.Select(p => new ProductDto
                {
                    Id = p.Id,
                    ProductCode = p.ProductCode,
                    Name = p.Name,
                    Quantity = p.Quantity,
                    Price = p.Price,
                    PrixAchat = p.PrixAchat,
                    SupplierId = p.SupplierId,
                    CreatedAt = p.CreatedAt
                }).ToList();

                _logger.LogInformation("Dashboard data fetched successfully. Low stock items: {Count}", lowStockDtos.Count);

                return Ok(new
                {
                    MostSellingProduct = topProductDto,
                    LowStockProducts = lowStockDtos
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