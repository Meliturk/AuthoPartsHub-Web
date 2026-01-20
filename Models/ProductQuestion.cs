using System;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class ProductQuestion
    {
        public int Id { get; set; }

        public int PartId { get; set; }
        public Part Part { get; set; } = null!;

        public int? UserId { get; set; }
        public AppUser? User { get; set; }

        [Required, StringLength(800)]
        public string Question { get; set; } = "";

        [StringLength(800)]
        public string? Answer { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AnsweredAt { get; set; }
    }
}
