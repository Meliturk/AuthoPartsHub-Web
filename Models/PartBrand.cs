using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public class PartBrand
    {
        public int Id { get; set; }

        [Required, StringLength(60)]
        public string Name { get; set; } = "";
    }
}
