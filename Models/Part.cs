using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class Part
    {
        public int Id { get; set; }

        [Required, StringLength(120)]
        public string Name { get; set; } = "";

        [Required, StringLength(60)]
        public string Brand { get; set; } = "";

        [Required, StringLength(40)]
        public string Category { get; set; } = "";

        [Range(0, 999999)]
        public decimal Price { get; set; }

        [Range(0, 999999)]
        public int Stock { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(400)]
        public string? ImageUrl { get; set; }

        [StringLength(40)]
        public string? Condition { get; set; } = "Sıfır"; // Sıfır / Çıkma / İkinci El / Yenilenmiş

        public int? SellerId { get; set; }
        public AppUser? Seller { get; set; }

        // Basit uyumluluk: şimdilik tek araç ile ilişki
        public int? VehicleId { get; set; }
        public Vehicle? Vehicle { get; set; }

        public List<PartVehicle> PartVehicles { get; set; } = new();
        public List<PartImage> PartImages { get; set; } = new();
    }
}
