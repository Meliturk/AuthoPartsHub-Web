using System;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class ProductReview
    {
        public int Id { get; set; }

        public int PartId { get; set; }
        public Part Part { get; set; } = null!;

        public int? UserId { get; set; }
        public AppUser? User { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        [StringLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
