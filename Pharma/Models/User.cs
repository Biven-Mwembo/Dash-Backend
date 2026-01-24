using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
namespace Pharma.Models
{
    [Table("users")]
    public class User : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; }  // Changed to string for Supabase UUID handling
        [Column("email")]
        public string Email { get; set; }
        [Column("role")]
        public string Role { get; set; }
        [Column("name")]
        public string Name { get; set; }
        [Column("surname")]
        public string Surname { get; set; }
    }

    // Models/Product.cs (UPDATED)
    [Table("products")]
    public class Product : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("product_code")]
        public string ProductCode { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("price")]
        public decimal Price { get; set; } // Selling Price (Prix Vente)

        [Column("prix_achat")] // New Column Mapping
        public decimal PrixAchat { get; set; } // Purchase Price (Prix Achat)

        [Column("supplier_id")]
        public string? SupplierId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Models/Sale.cs
    [Table("sales")]
    public class Sale : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("product_id")]
        public long ProductId { get; set; }  // Updated: Changed from string to long to match bigint in database

        [Column("quantity_sold")]
        public int QuantitySold { get; set; }

        [Column("sale_date")]
        public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    }

    // Models/Dashboard.cs (for responses)
    public class DashboardData
    {
        public Product MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; }
    }

    // Add this new DTO class for safe serialization (no Supabase attributes)
    // Models/ProductDto (UPDATED)
    public class ProductDto
    {
        public long Id { get; set; }
        public string ProductCode { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        // New DTO Property
        public decimal PrixAchat { get; set; }

        public string? SupplierId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SaleDto
    {
        public int Id { get; set; }
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
        public DateTime SaleDate { get; set; }
    }

    // New: DTO for sale items (used in checkout)
    public class SaleItemDto
    {
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
    }
}
