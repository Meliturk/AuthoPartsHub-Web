namespace AutoPartsWeb.Models
{
    public class PartVehicle
    {
        public int PartId { get; set; }
        public Part Part { get; set; } = null!;

        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;
    }
}
