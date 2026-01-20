using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class Vehicle
    {
        public int Id { get; set; }

        [Required, StringLength(60)]
        public string Brand { get; set; } = "";

        [Required, StringLength(60)]
        public string Model { get; set; } = "";

        public int Year { get; set; }

        // Üretim aralığı (isteğe bağlı). Doluyken Year yerine bu aralık kullanılır.
        public int? StartYear { get; set; }
        public int? EndYear { get; set; }

        [StringLength(60)]
        public string? Engine { get; set; }

        [StringLength(300)]
        public string? ImageUrl { get; set; }

        [StringLength(300)]
        public string? BrandLogoUrl { get; set; }

        public List<PartVehicle> PartVehicles { get; set; } = new();
    }
}
