using System;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class SellerApplication
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public AppUser User { get; set; } = null!;

        [Required, StringLength(160)]
        public string CompanyName { get; set; } = "";

        [Required, StringLength(160)]
        public string ContactName { get; set; } = "";

        [Required, StringLength(40)]
        public string Phone { get; set; } = "";

        [Required, StringLength(260)]
        public string Address { get; set; } = "";

        [StringLength(200)]
        public string? TaxNumber { get; set; }

        [StringLength(300)]
        public string? Note { get; set; }

        [Required, StringLength(30)]
        public string Status { get; set; } = "Pending"; // Pending / Approved / Rejected

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
