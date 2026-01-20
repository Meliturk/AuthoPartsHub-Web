using System.Collections.Generic;

namespace AutoPartsWeb.Models
{
    public class AdminDashboardVm
    {
        public int TotalParts { get; set; }
        public int TotalVehicles { get; set; }
        public int TotalUsers { get; set; }
        public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }

        public List<Part> LatestParts { get; set; } = new();
        public List<AdminChartPoint> SalesChartPoints { get; set; } = new();
        public List<AdminProductSales> TopProducts { get; set; } = new();
        public List<AdminSellerSales> TopSellers { get; set; } = new();
    }

    public class AdminChartPoint
    {
        public string Label { get; set; } = "";
        public decimal Total { get; set; }
    }

    public class AdminProductSales
    {
        public int PartId { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Total { get; set; }
    }

    public class AdminSellerSales
    {
        public int SellerId { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Total { get; set; }
    }
}
