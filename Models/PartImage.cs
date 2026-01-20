namespace AutoPartsWeb.Models
{
    public class PartImage
    {
        public int Id { get; set; }
        public int PartId { get; set; }
        public Part Part { get; set; } = null!;
        public string Url { get; set; } = "";
        public int SortOrder { get; set; } = 0;
    }
}
