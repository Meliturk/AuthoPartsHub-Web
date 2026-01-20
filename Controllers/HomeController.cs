using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AutoPartsWeb.Models;
using AutoPartsWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _db;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var suspendedSellers = await _db.AppUsers
            .Where(u => u.Role == "SellerSuspended")
            .Select(u => u.Id)
            .ToListAsync();

        var parts = await _db.Parts
            .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
            .Where(p => p.SellerId == null || !suspendedSellers.Contains(p.SellerId.Value))
            .OrderByDescending(p => p.Id)
            .Take(12)
            .ToListAsync();

        var partIds = parts.Select(p => p.Id).ToList();
        var ratingDict = await _db.ProductReviews
            .Where(r => partIds.Contains(r.PartId))
            .GroupBy(r => r.PartId)
            .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

        var vehicles = await _db.Vehicles
            .OrderBy(v => v.Brand)
            .ThenBy(v => v.Model)
            .ThenByDescending(v => v.Year)
            .ToListAsync();

        ViewBag.Brands = vehicles.Select(v => v.Brand).Distinct().OrderBy(b => b).ToList();
        // Model ve yıl listelerini marka/model bazlı dinamik filtreleme için gönderiyoruz
        ViewBag.VehicleData = vehicles
            .Select(v => new { v.Brand, v.Model, v.Year, v.StartYear, v.EndYear })
            .ToList();
        ViewBag.Ratings = ratingDict;

        return View(parts);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult About()
    {
    return View();
    }   

    public IActionResult Contact()
    {
    return View();
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Contact(string name, string email, string? phone, string message)
    {
        return RedirectToAction("Submit", "Contact", new { name, email, phone, message });
    }

}
