using System;
using System.Collections.Generic;

namespace AutoPartsWeb.Models
{
    public class SellerDashboardVm
    {
        public decimal DayTotal { get; set; }
        public decimal WeekTotal { get; set; }
        public decimal MonthTotal { get; set; }
        public decimal YearTotal { get; set; }
        public int OrdersCount { get; set; }
        public int PartsCount { get; set; }
        public List<SellerChartPoint> ChartPoints { get; set; } = new();
        public List<SellerProductSales> ProductSales { get; set; } = new();
    }

    public class SellerChartPoint
    {
        public string Label { get; set; } = "";
        public decimal Total { get; set; }
    }

    public class SellerProductSales
    {
        public int PartId { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Total { get; set; }
    }

}
