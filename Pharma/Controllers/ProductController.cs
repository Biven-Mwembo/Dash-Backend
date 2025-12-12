using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supabase;
using Product = Pharma.Models.Product;
using Sale = Pharma.Models.Sale;
using Pharma.Models; // For ProductDto and SaleItemDto

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly Supabase.Client _supabase;

    public ProductsController(Supabase.Client supabase) => _supabase = supabase;

    [HttpGet]
    public async Task<IActionResult> GetProducts()
    {
        try
        {
            Console.WriteLine("Fetching products...");
            var products = await _supabase.From<Product>().Get();
            Console.WriteLine($"Fetched {products.Models.Count} products.");
            if (!products.Models.Any())
            {
                Console.WriteLine("No products found.");
                return Ok(new List<ProductDto>()); // Return empty list
            }

            // Map to DTOs for safe serialization
            var productDtos = products.Models.Select(p => new ProductDto
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
            Console.WriteLine("Mapped products successfully.");
            return Ok(productDtos);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving products: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, $"Error retrieving products: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProduct(long id)
    {
        try
        {
            var product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
            if (product == null) return NotFound();
            // Map to DTO
            var productDto = new ProductDto
            {
                Id = product.Id,
                ProductCode = product.ProductCode,
                Name = product.Name,
                Quantity = product.Quantity,
                Price = product.Price,
                PrixAchat = product.PrixAchat,
                SupplierId = product.SupplierId,
                CreatedAt = product.CreatedAt
            };
            return Ok(productDto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving product: {ex.Message}");
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromBody] ProductDto productDto)  // Use DTO for input
    {
        try
        {
            // Map DTO to Supabase model
            var product = new Product
            {
                ProductCode = !string.IsNullOrEmpty(productDto.ProductCode) ? productDto.ProductCode : await GenerateNextProductCode(), // Use manual code if provided, else auto-generate
                Name = productDto.Name,
                Quantity = productDto.Quantity,
                Price = productDto.Price,
                PrixAchat = productDto.PrixAchat,
                SupplierId = productDto.SupplierId
            };

            var response = await _supabase.From<Product>().Insert(product);
            var createdProduct = response.Models.First();

            // Map back to DTO for response
            var resultDto = new ProductDto
            {
                Id = createdProduct.Id,
                ProductCode = createdProduct.ProductCode,
                Name = createdProduct.Name,
                Quantity = createdProduct.Quantity,
                Price = createdProduct.Price,
                PrixAchat = productDto.PrixAchat,
                SupplierId = createdProduct.SupplierId,
                CreatedAt = createdProduct.CreatedAt
            };

            return Ok(resultDto);
        }
        catch (Supabase.Postgrest.Exceptions.PostgrestException postgrestEx) when (postgrestEx.Message.Contains("violates row-level security policy"))
        {
            // Explicitly handle the RLS violation error (Code 42501)
            // Returning 403 Forbidden is more accurate for a permission issue.
            // The client will log the full error text.
            return StatusCode(403, $"Permission Denied: new row violates row-level security policy for table \"products\"");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error creating product: {ex.Message}");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(long id, [FromBody] ProductDto productDto) // Use DTO
    {
        try
        {
            // Map DTO to Supabase model
            var product = new Product
            {
                // 💡 CRITICAL FIX: Set the Primary Key (Id)
                Id = id,
                ProductCode = productDto.ProductCode,
                Name = productDto.Name,
                Quantity = productDto.Quantity,
                Price = productDto.Price,
                PrixAchat = productDto.PrixAchat,
                SupplierId = productDto.SupplierId
            };

            // This query is correct, but relies on the product object being correctly formed.
            await _supabase.From<Product>().Where(p => p.Id == id).Update(product);

            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error updating product: {ex.Message}");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(long id)
    {
        try
        {
            await _supabase.From<Product>().Where(p => p.Id == id).Delete();
            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error deleting product: {ex.Message}");
        }
    }

    [HttpPost("{id}/sell")]
    public async Task<IActionResult> SellProduct(long id, [FromBody] int quantitySold)
    {
        try
        {
            var product = await _supabase.From<Product>().Where(p => p.Id == id).Single();
            if (product == null) return NotFound("Product not found.");
            if (product.Quantity < quantitySold) return BadRequest("Insufficient stock.");

            // Update quantity
            product.Quantity -= quantitySold;
            await _supabase.From<Product>().Where(p => p.Id == id).Update(product);

            // Record sale
            await _supabase.From<Sale>().Insert(new Sale { ProductId = id, QuantitySold = quantitySold });  // Updated: Use id directly as long

            return Ok($"Sold {quantitySold} units of {product.Name}. Remaining quantity: {product.Quantity}");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error selling product: {ex.Message}");
        }
    }

    // Updated: Handle checkout for multiple cart items with better logging
    [HttpPost("sale")]
    public async Task<IActionResult> CreateSale([FromBody] List<SaleItemDto> saleItems)
    {
        try
        {
            Console.WriteLine("Starting checkout process...");
            foreach (var item in saleItems)
            {
                Console.WriteLine($"Processing item: ProductId={item.ProductId}, QuantitySold={item.QuantitySold}");

                // Fetch product to check stock and get details
                var product = await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Single();
                if (product == null)
                {
                    Console.WriteLine($"Product {item.ProductId} not found.");
                    return BadRequest($"Product {item.ProductId} not found.");
                }
                if (product.Quantity < item.QuantitySold)
                {
                    Console.WriteLine($"Insufficient stock for {product.Name}: Available {product.Quantity}, Requested {item.QuantitySold}");
                    return BadRequest($"Insufficient stock for {product.Name}.");
                }

                // Update quantity
                product.Quantity -= item.QuantitySold;
                await _supabase.From<Product>().Where(p => p.Id == item.ProductId).Update(product);
                Console.WriteLine($"Updated quantity for {product.Name} to {product.Quantity}");

                // Record sale
                await _supabase.From<Sale>().Insert(new Sale { ProductId = item.ProductId, QuantitySold = item.QuantitySold });  // Updated: Use item.ProductId directly as long
                Console.WriteLine($"Recorded sale for {product.Name}");
            }

            Console.WriteLine("Checkout completed successfully.");
            return Ok("Sale completed successfully.");
        }
        catch (Supabase.Postgrest.Exceptions.PostgrestException postgrestEx) when (postgrestEx.Message.Contains("violates row-level security policy"))
        {
            Console.WriteLine($"RLS violation: {postgrestEx.Message}");
            return StatusCode(403, $"Permission Denied: new row violates row-level security policy for table \"products\" or \"sales\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General error in CreateSale: {ex.Message}");
            return StatusCode(500, $"Error processing sale: {ex.Message}");
        }
    }

    [HttpGet("sales")]
    public async Task<IActionResult> GetSales()
    {
        try
        {
            Console.WriteLine("Fetching detailed sales...");
            var sales = await _supabase.From<Sale>().Get();
            var products = await _supabase.From<Product>().Get(); // Fetch all products for mapping

            Console.WriteLine($"Fetched {sales.Models.Count} sales and {products.Models.Count} products.");

            if (!sales.Models.Any())
            {
                Console.WriteLine("No sales found.");
                return Ok(new List<object>()); // Return empty list if no sales
            }

            var salesWithDetails = sales.Models.Select(s => {
                var product = products.Models.FirstOrDefault(p => p.Id == s.ProductId);  // Direct long comparison
                if (product == null)
                {
                    Console.WriteLine($"Product {s.ProductId} not found for sale {s.Id}.");
                    return null; // Skip if product not found
                }
                return new
                {
                    ProductName = product.Name,
                    QuantitySold = s.QuantitySold,
                    PricePerItem = product.Price,
                    TotalAmount = product.Price * s.QuantitySold,
                    SaleDate = s.SaleDate  // Added: Include sale date for Date and Time columns
                };
            }).Where(s => s != null).ToList(); // Filter out nulls

            Console.WriteLine("Mapped detailed sales successfully.");
            return Ok(salesWithDetails);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving detailed sales: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, $"Error retrieving detailed sales: {ex.Message}");
        }
    }

    [HttpGet("sales/daily")]
    public async Task<IActionResult> GetDailySales()
    {
        try
        {
            Console.WriteLine("Fetching daily sales...");
            var sales = await _supabase.From<Sale>().Get();
            var products = await _supabase.From<Product>().Get(); // Fetch all products for mapping

            Console.WriteLine($"Fetched {sales.Models.Count} sales and {products.Models.Count} products.");

            if (!sales.Models.Any())
            {
                Console.WriteLine("No sales found.");
                return Ok(new List<object>()); // Return empty list if no sales
            }

            // Aggregate sales by date
            var dailySales = sales.Models
                .GroupBy(s => s.SaleDate.Date)  // Group by date (ignore time)
                .Select(g => {
                    var totalAmount = g.Sum(s => {
                        var product = products.Models.FirstOrDefault(p => p.Id == s.ProductId);  // Direct long comparison
                        if (product == null)
                        {
                            Console.WriteLine($"Product {s.ProductId} not found for sale.");
                            return 0.00m; // Skip if product not found
                        }
                        return product.Price * s.QuantitySold;
                    });
                    return new
                    {
                        Date = g.Key,
                        Day = g.Key.ToString("dddd"),  // e.g., "Thursday"
                        NumberOfSales = g.Count(),
                        TotalAmount = totalAmount
                    };
                })
                .OrderByDescending(d => d.Date)  // Most recent first
                .ToList();

            Console.WriteLine("Aggregated daily sales successfully.");
            return Ok(dailySales);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving daily sales: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, $"Error retrieving daily sales: {ex.Message}");
        }
    }

    // Helper method to generate the next product code (e.g., PR001, PR002, ...)
    private async Task<string> GenerateNextProductCode()
    {
        try
        {
            // Query for the highest product_code starting with "PR"
            var products = await _supabase.From<Product>()
                .Where(p => p.ProductCode != null && p.ProductCode.StartsWith("PR"))
                .Order(p => p.ProductCode, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1)
                .Get();

            string nextCode;
            if (products.Models.Any())
            {
                var lastCode = products.Models.First().ProductCode; // e.g., "PR005"
                var numberPart = int.Parse(lastCode.Substring(2)); // Extract 5
                var nextNumber = numberPart + 1;
                nextCode = $"PR{nextNumber:D3}"; // Format as PR006 (3-digit padded)
            }
            else
            {
                nextCode = "PR001"; // Start from PR001 if none exist
            }

            return nextCode;
        }
        catch (Exception)
        {
            // Fallback in case of error
            return "PR001";
        }
    }
}
