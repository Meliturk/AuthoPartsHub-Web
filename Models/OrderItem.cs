using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public int PartId { get; set; }
        public Part Part { get; set; } = null!;

        [Range(1, 9999)]
        public int Quantity { get; set; }

        [Range(0, 999999)]
        public decimal UnitPrice { get; set; }
    }
}
