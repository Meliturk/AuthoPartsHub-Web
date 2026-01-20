using System.Security.Claims;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    [Authorize(Roles = "Seller")]
    public class SellerController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public SellerController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private int CurrentUserId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        public async Task<IActionResult> Index()
        {
            var sellerId = CurrentUserId;
            var now = DateTime.UtcNow;
            var orders = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .Where(o => o.Items.Any(i => i.Part.SellerId == sellerId))
                .ToListAsync();
            var salesOrders = orders
                .Where(o => o.Status != "Cancelled")
                .ToList();

            decimal Sum(Func<Order, bool> filter) =>
                salesOrders.Where(filter)
                      .SelectMany(o => o.Items.Where(i => i.Part.SellerId == sellerId))
                      .Sum(i => i.UnitPrice * i.Quantity);

            var vm = new SellerDashboardVm
            {
                DayTotal = Sum(o => o.CreatedAt >= now.AddDays(-1)),
                WeekTotal = Sum(o => o.CreatedAt >= now.AddDays(-7)),
                MonthTotal = Sum(o => o.CreatedAt >= now.AddMonths(-1)),
                YearTotal = Sum(o => o.CreatedAt >= now.AddYears(-1)),
                OrdersCount = salesOrders.Count,
                PartsCount = await _db.Parts.CountAsync(p => p.SellerId == sellerId)
            };

            var last7 = Enumerable.Range(0, 7)
                .Select(offset => now.Date.AddDays(-offset))
                .OrderBy(d => d)
                .ToList();

            foreach (var day in last7)
            {
                var total = salesOrders
                    .Where(o => o.CreatedAt.Date == day.Date)
                    .SelectMany(o => o.Items.Where(i => i.Part.SellerId == sellerId))
                    .Sum(i => i.UnitPrice * i.Quantity);
                vm.ChartPoints.Add(new SellerChartPoint
                {
                    Label = day.ToString("dd.MM"),
                    Total = total
                });
            }

            var recentItems = salesOrders
                .Where(o => o.CreatedAt >= now.AddDays(-30))
                .SelectMany(o => o.Items.Where(i => i.Part != null && i.Part.SellerId == sellerId))
                .ToList();

            vm.ProductSales = recentItems
                .GroupBy(i => new { i.PartId, Name = i.Part?.Name ?? $"Part #{i.PartId}" })
                .Select(g => new SellerProductSales
                {
                    PartId = g.Key.PartId,
                    Name = g.Key.Name,
                    Quantity = g.Sum(x => x.Quantity),
                    Total = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .OrderByDescending(x => x.Total)
                .Take(7)
                .ToList();

            return View(vm);
        }

        public async Task<IActionResult> Products()
        {
            var sellerId = CurrentUserId;
            var parts = await _db.Parts
                .Where(p => p.SellerId == sellerId)
                .Include(p => p.PartImages.OrderBy(pi => pi.SortOrder))
                .OrderByDescending(p => p.Id)
                .ToListAsync();
            ViewBag.Vehicles = await _db.Vehicles.OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year).ToListAsync();
            return View(parts);
        }

        public async Task<IActionResult> Questions(string? status)
        {
            var sellerId = CurrentUserId;
            var filter = (status ?? "").Trim();
            var showingAnswered = string.Equals(filter, "Answered", StringComparison.OrdinalIgnoreCase);

            var query = _db.ProductQuestions
                .Include(q => q.Part)
                .Include(q => q.User)
                .Where(q => q.Part.SellerId == sellerId);

            if (showingAnswered)
                query = query.Where(q => q.Answer != null && q.Answer != "");
            else
                query = query.Where(q => q.Answer == null || q.Answer == "");

            var qs = await query
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();

            ViewBag.FilterStatus = filter;
            ViewBag.ShowingAnswered = showingAnswered;
            return View(qs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustPartStock(int id, int delta)
        {
            var sellerId = CurrentUserId;
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == id && p.SellerId == sellerId);
            if (part == null) return NotFound();

            var newStock = part.Stock + delta;
            if (newStock < 0) newStock = 0;
            part.Stock = newStock;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Stok güncellendi.";
            return RedirectToAction(nameof(Products));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AnswerQuestion(int id, string answer)
        {
            var sellerId = CurrentUserId;
            var q = await _db.ProductQuestions
                .Include(x => x.Part)
                .FirstOrDefaultAsync(x => x.Id == id && x.Part.SellerId == sellerId);
            if (q == null) return NotFound();

            q.Answer = (answer ?? "").Trim();
            q.AnsweredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Yanıt kaydedildi.";
            return RedirectToAction(nameof(Questions));
        }

        [HttpGet]
        public async Task<IActionResult> CreatePart()
        {
            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();
            ViewBag.PartBrands = await GetPartBrandOptionsAsync();
            ViewBag.PartCategories = await GetPartCategoryOptionsAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePart(Part part, string? brandChoice, int[]? vehicleIds, string[]? startYears, string[]? endYears, IFormFile? ImageFile, IFormFile[]? GalleryFiles)
        {
            part.VehicleId = null;
            part.SellerId = CurrentUserId;
            part.Brand = ResolveBrand(part.Brand, brandChoice);
            if (string.IsNullOrWhiteSpace(part.Condition)) part.Condition = "Sıfır";

            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();
            ViewBag.PartBrands = await GetPartBrandOptionsAsync();
            ViewBag.PartCategories = await GetPartCategoryOptionsAsync();

            if (vehicleIds != null && vehicleIds.Length > 0)
            {
                var validIds = await _db.Vehicles.Select(v => v.Id).ToListAsync();
                foreach (var vid in vehicleIds)
                {
                    if (!validIds.Contains(vid))
                        ModelState.AddModelError("VehicleId", "Seçilen araç bulunamadı.");
                }
            }

            ModelState.Remove(nameof(Part.Brand));
            if (string.IsNullOrWhiteSpace(part.Brand))
            {
                ModelState.AddModelError(nameof(Part.Brand), "Marka gerekli.");
            }

            if (!ModelState.IsValid)
                return View(part);

            await AddPartBrandIfMissingAsync(part.Brand);

            var (uploadedPath, uploadError) = await SaveImageIfAny(ImageFile);
            if (!string.IsNullOrWhiteSpace(uploadError))
            {
                ModelState.AddModelError("ImageUrl", uploadError);
                return View(part);
            }
            if (!string.IsNullOrWhiteSpace(uploadedPath))
                part.ImageUrl = uploadedPath;

            _db.Parts.Add(part);
            await _db.SaveChangesAsync();

            if (GalleryFiles != null && GalleryFiles.Length > 0)
            {
                var currentMax = await _db.PartImages.Where(pi => pi.PartId == part.Id).Select(pi => (int?)pi.SortOrder).MaxAsync() ?? 0;
                foreach (var gf in GalleryFiles)
                {
                    var (gPath, gErr) = await SaveImageIfAny(gf);
                    if (!string.IsNullOrWhiteSpace(gErr))
                    {
                        TempData["Warning"] = $"Galeri görseli atlandı: {gErr}";
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(gPath))
                    {
                        currentMax++;
                        _db.PartImages.Add(new PartImage { PartId = part.Id, Url = gPath, SortOrder = currentMax });
                    }
                }
                await _db.SaveChangesAsync();
            }

            await SyncPartVehiclesWithRanges(part.Id, vehicleIds, startYears, endYears);

            TempData["Success"] = "Parça eklendi.";
            return RedirectToAction(nameof(Products));
        }

        [HttpGet]
        public async Task<IActionResult> EditPart(int id)
        {
            var sellerId = CurrentUserId;
            var part = await _db.Parts
                .Include(p => p.PartVehicles)
                .Include(p => p.PartImages.OrderBy(pi => pi.SortOrder))
                .FirstOrDefaultAsync(p => p.Id == id && p.SellerId == sellerId);
            if (part == null) return NotFound();

            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();

            return View(part);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPart(Part part, int[]? vehicleIds, string[]? startYears, string[]? endYears, IFormFile? ImageFile, IFormFile[]? GalleryFiles)
        {
            part.VehicleId = null;
            part.SellerId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(part.Condition)) part.Condition = "Sıfır";

            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();

            if (vehicleIds != null && vehicleIds.Length > 0)
            {
                var validIds = await _db.Vehicles.Select(v => v.Id).ToListAsync();
                foreach (var vid in vehicleIds)
                {
                    if (!validIds.Contains(vid))
                        ModelState.AddModelError("VehicleId", "Seçilen araç bulunamadı.");
                }
            }

            if (!ModelState.IsValid)
                return View(part);

            var (uploadedPath, uploadError) = await SaveImageIfAny(ImageFile);
            if (!string.IsNullOrWhiteSpace(uploadError))
            {
                ModelState.AddModelError("ImageUrl", uploadError);
                return View(part);
            }
            if (!string.IsNullOrWhiteSpace(uploadedPath))
                part.ImageUrl = uploadedPath;

            _db.Parts.Update(part);
            await _db.SaveChangesAsync();

            if (GalleryFiles != null && GalleryFiles.Length > 0)
            {
                var currentMax = await _db.PartImages.Where(pi => pi.PartId == part.Id).Select(pi => (int?)pi.SortOrder).MaxAsync() ?? 0;
                foreach (var gf in GalleryFiles)
                {
                    var (gPath, gErr) = await SaveImageIfAny(gf);
                    if (!string.IsNullOrWhiteSpace(gErr))
                    {
                        TempData["Warning"] = $"Galeri görseli atlandı: {gErr}";
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(gPath))
                    {
                        currentMax++;
                        _db.PartImages.Add(new PartImage { PartId = part.Id, Url = gPath, SortOrder = currentMax });
                    }
                }
                await _db.SaveChangesAsync();
            }

            await SyncPartVehiclesWithRanges(part.Id, vehicleIds, startYears, endYears);

            TempData["Success"] = "Parça güncellendi.";
            return RedirectToAction(nameof(Products));
        }

        public async Task<IActionResult> Orders()
        {
            var sellerId = CurrentUserId;
            var status = (Request.Query["status"].ToString() ?? "").Trim();
            var showingCompleted = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
            var showingCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            var query = _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .Where(o => o.Items.Any(i => i.Part.SellerId == sellerId));

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);
            else
                query = query.Where(o => o.Status != "Completed" && o.Status != "Cancelled");

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var list = orders.Select(o =>
            {
                var items = o.Items.Where(i => i.Part.SellerId == sellerId)
                    .Select(i => new OrderItemDto(i.PartId, i.Part.Name, i.Quantity, i.UnitPrice))
                    .ToList();

                return new SellerOrderDto(
                    o.Id,
                    o.CreatedAt,
                    o.Status,
                    o.CustomerName,
                    items.Sum(x => x.UnitPrice * x.Quantity),
                    items
                );
            }).ToList();

            ViewBag.FilterStatus = status;
            ViewBag.ShowingCompleted = showingCompleted;
            ViewBag.ShowingCancelled = showingCancelled;
            return View(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int id, string status)
        {
            var sellerId = CurrentUserId;
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            if (!order.Items.Any(i => i.Part.SellerId == sellerId))
            {
                return Forbid();
            }

            var allowed = new[] { "Pending", "Processing", "Shipped", "Cancelled", "Completed" };
            if (!allowed.Contains(status))
            {
                TempData["Warning"] = "Geçersiz durum.";
                return RedirectToAction(nameof(Orders));
            }

            order.Status = status;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Sipariş durumu güncellendi.";
            return RedirectToAction(nameof(Orders));
        }

        [HttpGet]
        public async Task<IActionResult> OrderDetail(int id)
        {
            var sellerId = CurrentUserId;
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            if (!order.Items.Any(i => i.Part.SellerId == sellerId)) return Forbid();

            // sadece bu satıcıya ait kalemleri göster
            order.Items = order.Items.Where(i => i.Part.SellerId == sellerId).ToList();
            return View(order);
        }

        private async Task SyncPartVehicles(int partId, int[]? vehicleIds)
        {
            var ids = (vehicleIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
            var existing = await _db.PartVehicles.Where(pv => pv.PartId == partId).ToListAsync();

            var toRemove = existing.Where(e => !ids.Contains(e.VehicleId)).ToList();
            _db.PartVehicles.RemoveRange(toRemove);

            var toAddIds = ids.Where(id => !existing.Any(e => e.VehicleId == id)).ToList();
            foreach (var vid in toAddIds)
            {
                _db.PartVehicles.Add(new PartVehicle { PartId = partId, VehicleId = vid });
            }

            await _db.SaveChangesAsync();
        }

        private async Task SyncPartVehiclesWithRanges(int partId, int[]? vehicleIds, string[]? startYears, string[]? endYears)
        {
            var ids = (vehicleIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
            var existing = await _db.PartVehicles.Where(pv => pv.PartId == partId).ToListAsync();

            var toRemove = existing.Where(e => !ids.Contains(e.VehicleId)).ToList();
            _db.PartVehicles.RemoveRange(toRemove);

            var sy = startYears ?? Array.Empty<string>();
            var ey = endYears ?? Array.Empty<string>();

            foreach (var vid in ids)
            {
                var idx = Array.IndexOf(vehicleIds ?? Array.Empty<int>(), vid);
                int? sYear = null;
                int? eYear = null;
                if (idx >= 0 && idx < sy.Length && idx < ey.Length)
                {
                    if (int.TryParse(sy[idx], out var sv)) sYear = sv;
                    if (int.TryParse(ey[idx], out var ev)) eYear = ev;
                }

                if (!existing.Any(e => e.VehicleId == vid))
                {
                    _db.PartVehicles.Add(new PartVehicle { PartId = partId, VehicleId = vid });
                }

                var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vid);
                if (vehicle != null)
                {
                    if (sYear.HasValue && eYear.HasValue && sYear.Value <= eYear.Value)
                    {
                        vehicle.StartYear = sYear;
                        vehicle.EndYear = eYear;
                    }
                    else if (!vehicle.StartYear.HasValue || !vehicle.EndYear.HasValue)
                    {
                        vehicle.StartYear = vehicle.Year;
                        vehicle.EndYear = vehicle.Year;
                    }
                }
            }

            await _db.SaveChangesAsync();
        }

        private static string ResolveBrand(string? brandInput, string? brandChoice)
        {
            var choice = (brandChoice ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(choice) && !string.Equals(choice, "__custom__", StringComparison.OrdinalIgnoreCase))
                return choice;

            return (brandInput ?? "").Trim();
        }

        private async Task<List<string>> GetPartBrandOptionsAsync()
        {
            var partBrands = await _db.Parts.Select(p => p.Brand).ToListAsync();
            var lookupBrands = await _db.PartBrands.Select(b => b.Name).ToListAsync();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in partBrands.Concat(lookupBrands))
            {
                var value = (name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                set.Add(value);
            }

            return set.OrderBy(x => x).ToList();
        }

        private async Task<List<string>> GetPartCategoryOptionsAsync()
        {
            var categories = await _db.Parts.Select(p => p.Category).ToListAsync();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in categories)
            {
                var value = (name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                set.Add(value);
            }

            return set.OrderBy(x => x).ToList();
        }

        private async Task AddPartBrandIfMissingAsync(string? brand)
        {
            var name = (brand ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var exists = await _db.PartBrands.AnyAsync(b => b.Name.ToLower() == name.ToLower());
            if (!exists)
                _db.PartBrands.Add(new PartBrand { Name = name });
        }

        private async Task<(string? path, string? error)> SaveImageIfAny(IFormFile? file)
        {
            if (file == null || file.Length == 0) return (null, null);

            var maxBytes = 2 * 1024 * 1024;
            if (file.Length > maxBytes)
                return (null, "Dosya boyutu 2MB'yi geçemez.");

            var allowedExt = new[] { ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExt.Contains(ext))
                return (null, "Sadece .webp görsel yükleyebilirsiniz.");

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            return ($"/uploads/{fileName}", null);
        }
    }
}
