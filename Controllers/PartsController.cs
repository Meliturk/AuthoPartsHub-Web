using AutoPartsWeb.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutoPartsWeb.Models;
using System.Security.Claims;

namespace AutoPartsWeb.Controllers
{
    public class PartsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public PartsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: /Parts
        public async Task<IActionResult> Index(
            string? q,
            string? category,
            string[]? categoryList,
            int? vehicleId,
            string? brand,
            string? model,
            int? year,
            decimal? minPrice,
            decimal? maxPrice,
            string? sort,
            string? partBrand,
            string[]? partBrandList,
            string[]? brandList,
            string[]? modelList,
            int[]? yearList)
        {
            q = (q ?? "").Trim();
            category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();
            model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
            partBrand = string.IsNullOrWhiteSpace(partBrand) ? null : partBrand.Trim();
            sort = (sort ?? "").Trim();

            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (categoryList != null)
            {
                foreach (var c in categoryList.Where(x => !string.IsNullOrWhiteSpace(x)))
                    selectedCategories.Add(c.Trim());
            }
            if (!string.IsNullOrWhiteSpace(category))
                selectedCategories.Add(category);

            var selectedPartBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (partBrandList != null)
            {
                foreach (var b in partBrandList.Where(x => !string.IsNullOrWhiteSpace(x)))
                    selectedPartBrands.Add(b.Trim());
            }
            if (!string.IsNullOrWhiteSpace(partBrand))
                selectedPartBrands.Add(partBrand);

            var selectedBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (brandList != null) foreach (var b in brandList.Where(x => !string.IsNullOrWhiteSpace(x))) selectedBrands.Add(b.Trim());
            if (!string.IsNullOrWhiteSpace(brand)) selectedBrands.Add(brand);

            var selectedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (modelList != null) foreach (var m in modelList.Where(x => !string.IsNullOrWhiteSpace(x))) selectedModels.Add(m.Trim());
            if (!string.IsNullOrWhiteSpace(model)) selectedModels.Add(model);

            var selectedYears = new HashSet<int>();
            if (yearList != null) foreach (var y in yearList) selectedYears.Add(y);
            if (year.HasValue) selectedYears.Add(year.Value);

            var suspendedSellers = await _db.AppUsers
                .Where(u => u.Role == "SellerSuspended")
                .Select(u => u.Id)
                .ToListAsync();

            var query = _db.Parts
                .Include(p => p.Vehicle)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .AsQueryable();

            if (suspendedSellers.Count > 0)
            {
                query = query.Where(p => p.SellerId == null || !suspendedSellers.Contains(p.SellerId.Value));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                query = query.Where(p =>
                    EF.Functions.Like(p.Name, like) ||
                    EF.Functions.Like(p.Brand, like) ||
                    EF.Functions.Like(p.Category, like)
                );
            }

            if (selectedCategories.Any())
            {
                query = query.Where(p => selectedCategories.Contains(p.Category));
            }

            // Araç uyumluluğu: öncelik direct vehicleId, ardından marka/model/yıl kombinasyonu
            if (vehicleId.HasValue && vehicleId > 0)
            {
                query = query.Where(p =>
                    p.PartVehicles.Any(pv => pv.VehicleId == vehicleId.Value) ||
                    (p.VehicleId != null && p.VehicleId == vehicleId.Value));
            }
            else if (selectedBrands.Any() || selectedModels.Any() || selectedYears.Any())
            {
                var vehiclesQuery = _db.Vehicles.AsQueryable();
                if (selectedBrands.Any())
                    vehiclesQuery = vehiclesQuery.Where(v => selectedBrands.Contains(v.Brand));
                if (selectedModels.Any())
                    vehiclesQuery = vehiclesQuery.Where(v => selectedModels.Contains(v.Model));
                if (selectedYears.Any())
                    vehiclesQuery = vehiclesQuery.Where(v =>
                        (v.StartYear.HasValue && v.EndYear.HasValue && selectedYears.Any(y => v.StartYear.Value <= y && v.EndYear.Value >= y)) ||
                        (!v.StartYear.HasValue && selectedYears.Contains(v.Year)));

                var matchedIds = await vehiclesQuery.Select(v => v.Id).ToListAsync();
                if (matchedIds.Count > 0)
                {
                    query = query.Where(p =>
                        p.PartVehicles.Any(pv => matchedIds.Contains(pv.VehicleId)) ||
                        (p.VehicleId != null && matchedIds.Contains(p.VehicleId.Value)));
                }
                else
                {
                    // Hiç araç eşleşmediyse boş sonuç döndürmek için sahte id
                    query = query.Where(p => false);
                }
            }

            // Parça markası listesi (mevcut filtrelere göre)
            var partBrandOptions = await query
                .Select(p => p.Brand)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct()
                .OrderBy(b => b)
                .ToListAsync();

            if (selectedPartBrands.Any())
            {
                query = query.Where(p => selectedPartBrands.Contains(p.Brand));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(p => p.Price <= maxPrice.Value);
            }

            // veri çekimi
            var parts = await query.ToListAsync();

            // sıralama (client-side, SQLite decimal ORDER BY hatasını önlemek için)
            parts = sort switch
            {
                "price_asc" => parts.OrderBy(p => p.Price).ToList(),
                "price_desc" => parts.OrderByDescending(p => p.Price).ToList(),
                "name_asc" => parts.OrderBy(p => p.Name).ToList(),
                "name_desc" => parts.OrderByDescending(p => p.Name).ToList(),
                _ => parts.OrderByDescending(p => p.Id).ToList() // default: newest/featured
            };

            var partIds = parts.Select(p => p.Id).ToList();
            var ratingDict = await _db.ProductReviews
                .Where(r => partIds.Contains(r.PartId))
                .GroupBy(r => r.PartId)
                .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

            ViewBag.CartCount = GetCartCount();
            ViewBag.Filters = new
            {
                q,
                category,
                vehicleId,
                brand,
                model,
                year,
                minPrice,
                maxPrice,
                sort,
                partBrand,
                partBrandList = selectedPartBrands.ToList(),
                categoryList = selectedCategories.ToList(),
                brandList = selectedBrands.ToList(),
                modelList = selectedModels.ToList(),
                yearList = selectedYears.ToList()
            };

            var vehicleList = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .Select(v => new { v.Id, v.Brand, v.Model, v.Year, v.StartYear, v.EndYear })
                .ToListAsync();
            ViewBag.Vehicles = vehicleList;
            ViewBag.Categories = await _db.Parts.Select(p => p.Category).Distinct().ToListAsync();
            ViewBag.PartBrands = partBrandOptions;
            ViewBag.Ratings = ratingDict;

            // brand-model-year filtre listeleri
            var filterBrands = vehicleList.Select(v => v.Brand).Distinct().OrderBy(v => v).ToList();
            var filterModels = vehicleList
                .Where(v => string.IsNullOrWhiteSpace(brand) || v.Brand == brand)
                .Select(v => v.Model)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            var filterYearsSet = new HashSet<int>();
            foreach (var v in vehicleList)
            {
                if (!string.IsNullOrWhiteSpace(brand) && v.Brand != brand) continue;
                if (!string.IsNullOrWhiteSpace(model) && v.Model != model) continue;

                if (v.StartYear.HasValue && v.EndYear.HasValue)
                {
                    for (int y = v.StartYear.Value; y <= v.EndYear.Value; y++)
                        filterYearsSet.Add(y);
                }
                else
                {
                    filterYearsSet.Add(v.Year);
                }
            }
            var filterYears = filterYearsSet.OrderByDescending(y => y).ToList();

            ViewBag.FilterBrands = filterBrands;
            ViewBag.FilterModels = filterModels;
            ViewBag.FilterYears = filterYears;
            ViewBag.VehicleJson = System.Text.Json.JsonSerializer.Serialize(vehicleList,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            return View(parts);
        }

        // GET: /Parts/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var part = await _db.Parts
                .Include(p => p.Vehicle)
                .Include(p => p.Seller)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .Include(p => p.PartImages)
                .Include(p => p.PartImages)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (part == null) return NotFound();

            // Benzer ürünler: aynı marka ve model uyumluluğuna sahip diğer parçalar
            var matchBrand = part.Brand;
            var matchModels = part.PartVehicles?
                .Select(pv => pv.Vehicle?.Model)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m!)
                .Distinct()
                .ToList() ?? new List<string>();

            var similarQuery = _db.Parts
                .Include(p => p.PartImages)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .Where(p => p.Id != id);

            if (!string.IsNullOrWhiteSpace(matchBrand))
                similarQuery = similarQuery.Where(p => p.Brand == matchBrand);
            if (matchModels.Count > 0)
                similarQuery = similarQuery.Where(p => p.PartVehicles.Any(pv => matchModels.Contains(pv.Vehicle!.Model)));

            var similar = await similarQuery
                .OrderByDescending(p => p.Id)
                .Take(4)
                .ToListAsync();

            // Eğer benzer bulunmazsa: en yeni 4 üründen fallback göster
            if (similar.Count == 0)
            {
                similar = await _db.Parts
                    .Where(p => p.Id != id)
                    .Include(p => p.PartImages)
                    .OrderByDescending(p => p.Id)
                    .Take(4)
                    .ToListAsync();
            }

            // Benzerlerde olmayan rastgele ürünler (keşfet)
            var excludeIds = similar.Select(s => s.Id).Append(id).ToList();
            var randomPool = await _db.Parts
                .Where(p => !excludeIds.Contains(p.Id))
                .Include(p => p.PartImages)
                .ToListAsync();
            var randoms = randomPool
                .OrderBy(_ => Guid.NewGuid())
                .Take(8)
                .ToList();

            var questions = await _db.ProductQuestions
                .Where(q => q.PartId == id)
                .Include(q => q.User)
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();

            var reviews = await _db.ProductReviews
                .Where(r => r.PartId == id)
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            var avg = reviews.Count > 0 ? reviews.Average(r => r.Rating) : 0;

            var similarIds = similar.Select(s => s.Id).ToList();
            var discoverIds = randoms.Select(s => s.Id).ToList();
            var simReviewDict = await _db.ProductReviews
                .Where(r => similarIds.Contains(r.PartId))
                .GroupBy(r => r.PartId)
                .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));
            var disReviewDict = await _db.ProductReviews
                .Where(r => discoverIds.Contains(r.PartId))
                .GroupBy(r => r.PartId)
                .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

            ViewBag.Similar = similar;
            ViewBag.Discover = randoms;
            ViewBag.CartCount = GetCartCount();
            ViewBag.Questions = questions;
            ViewBag.Reviews = reviews;
            ViewBag.AvgRating = avg;
            ViewBag.SimilarReviews = simReviewDict;
            ViewBag.DiscoverReviews = disReviewDict;
            return View(part);
        }

        private int GetCartCount()
        {
            var json = HttpContext.Session.GetString("CartItems");
            if (string.IsNullOrWhiteSpace(json)) return 0;
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<Models.CartItem>>(json);
                return list?.Sum(x => x.Quantity) ?? 0;
            }
            catch { return 0; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> AskQuestion(int partId, string question)
        {
            question = (question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                TempData["Warning"] = "Soru boş olamaz.";
                return RedirectToAction(nameof(Details), new { id = partId });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? userId = null;
            if (int.TryParse(userIdStr, out var uid))
                userId = uid;

            _db.ProductQuestions.Add(new ProductQuestion
            {
                PartId = partId,
                UserId = userId,
                Question = question,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Sorunuz iletildi.";
            return RedirectToAction(nameof(Details), new { id = partId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> AddReview(int partId, int rating, string? comment)
        {
            rating = Math.Clamp(rating, 1, 5);
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            int? userId = null;
            if (int.TryParse(userIdStr, out var uid))
                userId = uid;

            // Satın alma kontrolü: tamamlanmış bir siparişte bu parçayı almış mı?
            var purchased = !string.IsNullOrWhiteSpace(userEmail) && await _db.Orders
                .Include(o => o.Items)
                .AnyAsync(o =>
                    o.Status == "Completed" &&
                    o.Email == userEmail &&
                    o.Items.Any(i => i.PartId == partId));

            if (!purchased)
            {
                TempData["Warning"] = "Yorum/puan verebilmek için ürünü satın almış olmanız ve siparişin tamamlanmış olması gerekir.";
                return RedirectToAction(nameof(Details), new { id = partId });
            }

            // Kullanıcı başına tek değerlendirme
            if (userId.HasValue)
            {
                var already = await _db.ProductReviews.AnyAsync(r => r.PartId == partId && r.UserId == userId.Value);
                if (already)
                {
                    TempData["Warning"] = "Bu ürüne zaten değerlendirme yaptınız.";
                    return RedirectToAction(nameof(Details), new { id = partId });
                }
            }

            _db.ProductReviews.Add(new ProductReview
            {
                PartId = partId,
                UserId = userId,
                Rating = rating,
                Comment = (comment ?? "").Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Değerlendirmeniz kaydedildi.";
            return RedirectToAction(nameof(Details), new { id = partId });
        }

        public async Task<IActionResult> Seed()
    {
        if (!await _db.Vehicles.AnyAsync())
        {
            _db.Vehicles.AddRange(
                new AutoPartsWeb.Models.Vehicle { Brand = "Fiat", Model = "Egea", Year = 2020, Engine = "1.3 Multijet" },
                new AutoPartsWeb.Models.Vehicle { Brand = "Volkswagen", Model = "Golf", Year = 2019, Engine = "1.6 TDI" }
            );
            await _db.SaveChangesAsync();
        }
    
        var v1 = await _db.Vehicles.FirstAsync();
        var v2 = await _db.Vehicles.OrderByDescending(x => x.Id).FirstAsync();
    
        if (!await _db.Parts.AnyAsync())
        {
            _db.Parts.AddRange(
                new AutoPartsWeb.Models.Part { Name = "Fren Balatası", Brand = "Bosch", Category = "Fren", Price = 1250, Stock = 24, VehicleId = v1.Id },
                new AutoPartsWeb.Models.Part { Name = "O2 Sensörü", Brand = "NGK", Category = "Motor", Price = 2990, Stock = 3, VehicleId = v2.Id },
                new AutoPartsWeb.Models.Part { Name = "Yağ Filtresi", Brand = "MANN", Category = "Filtre", Price = 290, Stock = 12, VehicleId = v1.Id },
                new AutoPartsWeb.Models.Part { Name = "Amortisör", Brand = "Monroe", Category = "Süspansiyon", Price = 1890, Stock = 0, VehicleId = v2.Id }
            );
            await _db.SaveChangesAsync();
        }
    
        return RedirectToAction("Index");
        }
    

    }
}
