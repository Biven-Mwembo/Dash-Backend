using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Pharma.Models
{
    [Table("users")]
    public class User : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; } = Guid.NewGuid(); // ✅ Changed from string to Guid

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("role")]
        public string Role { get; set; } = "user";

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("surname")]
        public string Surname { get; set; } = string.Empty;

        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }

    [Table("products")]
    public class Product : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("product_code")]
        public string ProductCode { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("price")]
        public decimal Price { get; set; }

        [Column("prix_achat")]
        public decimal PrixAchat { get; set; }

        [Column("supplier_id")]
        public string? SupplierId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("sales")]
    public class Sale : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("product_id")]
        public long ProductId { get; set; }

        [Column("quantity_sold")]
        public int QuantitySold { get; set; }

        [Column("sale_date")]
        public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    }

    public class DashboardData
    {
        // Added ? to make these nullable and avoid initialization warnings
        public Product? MostSellingProduct { get; set; }
        public List<Product> LowStockProducts { get; set; } = new();
    }

    public class ProductDto
    {
        public long Id { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
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

    public class SaleItemDto
    {
        public long ProductId { get; set; }
        public int QuantitySold { get; set; }
    }
}