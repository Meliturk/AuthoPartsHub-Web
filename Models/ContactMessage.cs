using System;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class ContactMessage
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required, StringLength(160)]
        public string Name { get; set; } = "";

        [Required, StringLength(200)]
        public string Email { get; set; } = "";

        [StringLength(40)]
        public string? Phone { get; set; }

        [Required, StringLength(1000)]
        public string Message { get; set; } = "";

        [StringLength(30)]
        public string Status { get; set; } = "Yeni"; // Yeni / Okundu
    }
}
