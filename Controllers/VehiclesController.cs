using AutoPartsWeb.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    public class VehiclesController : Controller
    {
        private readonly ApplicationDbContext _db;
        public VehiclesController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(string? q, string? brand, string? model, int? year, string? sort)
        {
            q = (q ?? "").Trim();
            brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();
            model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
            sort = (sort ?? "").Trim();

            var allVehicles = await _db.Vehicles.ToListAsync();
            var query = allVehicles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                query = query.Where(v =>
                    EF.Functions.Like(v.Brand, like) ||
                    EF.Functions.Like(v.Model, like) ||
                    EF.Functions.Like(v.Engine ?? "", like));
            }

            if (!string.IsNullOrWhiteSpace(brand))
                query = query.Where(v => v.Brand == brand);

            if (!string.IsNullOrWhiteSpace(model))
                query = query.Where(v => v.Model == model);

            if (year.HasValue && year > 0)
            {
                var y = year.Value;
                query = query.Where(v =>
                    (v.StartYear.HasValue && v.EndYear.HasValue && v.StartYear.Value <= y && v.EndYear.Value >= y) ||
                    (!v.StartYear.HasValue && !v.EndYear.HasValue && v.Year == y) ||
                    (v.StartYear.HasValue && !v.EndYear.HasValue && v.StartYear.Value == y) ||
                    (!v.StartYear.HasValue && v.EndYear.HasValue && v.EndYear.Value == y));
            }

            query = sort switch
            {
                "year_desc" => query.OrderByDescending(v => v.Year).ThenBy(v => v.Brand).ThenBy(v => v.Model),
                "year_asc" => query.OrderBy(v => v.Year).ThenBy(v => v.Brand).ThenBy(v => v.Model),
                "brand_az" => query.OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenByDescending(v => v.Year),
                "brand_za" => query.OrderByDescending(v => v.Brand).ThenBy(v => v.Model).ThenByDescending(v => v.Year),
                _ => query.OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenByDescending(v => v.Year)
            };

            var vehicles = query.ToList();

            var slimVehicles = allVehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .Select(v => new { v.Id, v.Brand, v.Model, v.Year, v.StartYear, v.EndYear, v.ImageUrl })
                .ToList();

            ViewBag.AllVehicles = slimVehicles;
            ViewBag.Brands = allVehicles.Select(v => v.Brand).Distinct().OrderBy(v => v).ToList();
            ViewBag.Models = allVehicles
                .Where(v => string.IsNullOrWhiteSpace(brand) || v.Brand == brand)
                .Select(v => v.Model)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            ViewBag.Years = allVehicles.Select(v => v.Year).Distinct().OrderByDescending(y => y).ToList();
            ViewBag.Q = q;
            ViewBag.SelectedBrand = brand;
            ViewBag.SelectedModel = model;
            ViewBag.SelectedYear = year;
            ViewBag.Sort = sort;
            ViewBag.VehiclesJson = System.Text.Json.JsonSerializer.Serialize(slimVehicles,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            return View(vehicles);
        }
    }
}
