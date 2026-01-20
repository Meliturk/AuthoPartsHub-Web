using System.Collections.Generic;

namespace AutoPartsWeb.Models
{
    public class AdminUsersVm
    {
        public List<AppUser> Customers { get; set; } = new();
        public List<AppUser> Sellers { get; set; } = new();
        public List<SellerApplication> SellerApplications { get; set; } = new();
        public List<SellerApplication> PendingApplications { get; set; } = new();
    }
}
