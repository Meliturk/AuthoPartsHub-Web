using System;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required, StringLength(120)]
        public string FullName { get; set; } = "";

        [Required, StringLength(160)]
        public string Email { get; set; } = "";

        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = "";

        public bool EmailConfirmed { get; set; }

        [StringLength(200)]
        public string? EmailConfirmTokenHash { get; set; }

        public DateTime? EmailConfirmExpiresAt { get; set; }

        [StringLength(200)]
        public string? PasswordResetTokenHash { get; set; }

        public DateTime? PasswordResetExpiresAt { get; set; }

        [Required, StringLength(20)]
        public string Role { get; set; } = "User"; // User / Admin / Seller / SellerPending
    }
}
