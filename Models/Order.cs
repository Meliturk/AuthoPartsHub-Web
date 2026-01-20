using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required, StringLength(160)]
        public string CustomerName { get; set; } = "";

        [Required, StringLength(200)]
        public string Email { get; set; } = "";

        [Required, StringLength(200)]
        public string Address { get; set; } = "";

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(40)]
        public string? Phone { get; set; }

        [Required, StringLength(30)]
        public string Status { get; set; } = "Pending";

        [Range(0, 99999999)]
        public decimal Total { get; set; }

        public List<OrderItem> Items { get; set; } = new();
    }
}
