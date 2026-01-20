namespace AutoPartsWeb.Models
{
    public class CartItem
    {
        public int PartId { get; set; }
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string? VehicleText { get; set; }
    }
}
