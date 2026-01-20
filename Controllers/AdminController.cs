using System.IO;
using AutoPartsWeb.Data;
using System.Text;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private const string DefaultAdminEmail = "admin@site.com";

        public AdminController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // =========================
        // DASHBOARD
        // =========================
        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var salesItemsQuery = _db.OrderItems
                .Where(i => i.Order.Status != "Cancelled");

            var totalSales = await salesItemsQuery
                .Select(i => (decimal?)(i.UnitPrice * i.Quantity))
                .SumAsync() ?? 0m;

            var totalOrders = await _db.Orders
                .CountAsync(o => o.Status != "Cancelled");

            var dailyTotals = await salesItemsQuery
                .Where(i => i.Order.CreatedAt >= now.Date.AddDays(-6))
                .GroupBy(i => i.Order.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Total = g.Sum(x => x.UnitPrice * x.Quantity) })
                .ToListAsync();

            var last7 = Enumerable.Range(0, 7)
                .Select(offset => now.Date.AddDays(-offset))
                .OrderBy(d => d)
                .ToList();

            var salesChartPoints = new List<AdminChartPoint>();
            foreach (var day in last7)
            {
                var total = dailyTotals.FirstOrDefault(x => x.Day.Date == day.Date)?.Total ?? 0m;
                salesChartPoints.Add(new AdminChartPoint
                {
                    Label = day.ToString("dd.MM"),
                    Total = total
                });
            }

            var topProducts = (await salesItemsQuery
                .GroupBy(i => new { i.PartId, i.Part.Name })
                .Select(g => new AdminProductSales
                {
                    PartId = g.Key.PartId,
                    Name = g.Key.Name ?? $"Part #{g.Key.PartId}",
                    Quantity = g.Sum(x => x.Quantity),
                    Total = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .ToListAsync())
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToList();

            var topSellerRaw = (await salesItemsQuery
                .Where(i => i.Part.SellerId != null)
                .GroupBy(i => i.Part.SellerId)
                .Select(g => new
                {
                    SellerId = g.Key!.Value,
                    Quantity = g.Sum(x => x.Quantity),
                    Total = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .ToListAsync())
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToList();

            var sellerIds = topSellerRaw.Select(x => x.SellerId).ToList();
            var sellerNames = await _db.AppUsers
                .Where(u => sellerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName);

            var topSellers = topSellerRaw
                .Select(x => new AdminSellerSales
                {
                    SellerId = x.SellerId,
                    Name = sellerNames.TryGetValue(x.SellerId, out var name) ? name : $"Seller #{x.SellerId}",
                    Quantity = x.Quantity,
                    Total = x.Total
                })
                .ToList();

            var vm = new AdminDashboardVm
            {
                TotalParts = await _db.Parts.CountAsync(),
                TotalVehicles = await _db.Vehicles.CountAsync(),
                TotalUsers = await _db.AppUsers.CountAsync(),
                TotalSales = totalSales,
                TotalOrders = totalOrders,
                SalesChartPoints = salesChartPoints,
                TopProducts = topProducts,
                TopSellers = topSellers,
                LatestParts = await _db.Parts
                    .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                    .OrderByDescending(p => p.Id)
                    .Take(5)
                    .ToListAsync()
            };

            return View(vm);
        }

        // =========================
        // ORDERS - LIST / DETAIL
        // =========================
        public async Task<IActionResult> Orders()
        {
            var status = (Request.Query["status"].ToString() ?? "").Trim();
            var q = (Request.Query["q"].ToString() ?? "").Trim();
            DateTime? from = null, to = null;
            if (DateTime.TryParse(Request.Query["from"], out var f)) from = f;
            if (DateTime.TryParse(Request.Query["to"], out var t)) to = t;
            var query = _db.Orders.AsQueryable();
            var showingCompleted = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
            var showingCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);
            else
                query = query.Where(o => o.Status != "Completed" && o.Status != "Cancelled"); // varsayılan: tamamlanan + iptal gizle

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(o => EF.Functions.Like(o.CustomerName, $"%{q}%") || EF.Functions.Like(o.Email, $"%{q}%"));

            if (from.HasValue)
                query = query.Where(o => o.CreatedAt >= from.Value);
            if (to.HasValue)
                query = query.Where(o => o.CreatedAt <= to.Value);

            query = query.Include(o => o.Items).ThenInclude(i => i.Part);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.FilterStatus = status;
            ViewBag.ShowingCompleted = showingCompleted;
            ViewBag.ShowingCancelled = showingCancelled;
            ViewBag.Q = q;
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");
            return View(orders);
        }

        public async Task<IActionResult> OrderDetail(int id)
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int id, string status)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            var allowed = new[] { "Pending", "Processing", "Shipped", "Cancelled", "Completed" };
            if (!allowed.Contains(status))
            {
                TempData["Warning"] = "Geçersiz durum.";
                return RedirectToAction(nameof(OrderDetail), new { id });
            }

            order.Status = status;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Sipariş durumu güncellendi.";
            return RedirectToAction(nameof(OrderDetail), new { id });
        }


        // =========================
        // PARTS - LIST (DB SEARCH)
        // =========================
        public async Task<IActionResult> Parts(string? q)
        {
            q = (q ?? "").Trim();

            var query = _db.Parts
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";

                query = query.Where(p =>
                    EF.Functions.Like(p.Name, like) ||
                    EF.Functions.Like(p.Brand, like) ||
                    EF.Functions.Like(p.Category, like) ||
                    p.PartVehicles.Any(pv =>
                        EF.Functions.Like(pv.Vehicle.Brand, like) ||
                        EF.Functions.Like(pv.Vehicle.Model, like) ||
                        EF.Functions.Like(pv.Vehicle.Year.ToString(), like)
                    )
                );
            }

            var list = await query
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            ViewBag.Q = q;
            return View(list); // Views/Admin/Parts.cshtml
        }

        // =========================
        // PARTS - CREATE
        // =========================
        [HttpGet]
        public async Task<IActionResult> CreatePart()
        {
            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();
            ViewBag.PartBrands = await GetPartBrandOptionsAsync();
            ViewBag.PartCategories = await GetPartCategoryOptionsAsync();

            return View(); // Views/Admin/CreatePart.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePart(Part part, string? brandChoice, int[]? vehicleIds, string[]? startYears, string[]? endYears, IFormFile? ImageFile, IFormFile[]? GalleryFiles)
        {
            part.VehicleId = null;
            part.Brand = ResolveBrand(part.Brand, brandChoice);

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
            return RedirectToAction("CreatePart");
        }

        // =========================
        // PARTS - EDIT
        // =========================
        [HttpGet]
        public async Task<IActionResult> EditPart(int id)
        {
            var part = await _db.Parts
                .Include(p => p.PartVehicles)
                .Include(p => p.PartImages.OrderBy(pi => pi.SortOrder))
                .FirstOrDefaultAsync(p => p.Id == id);
            if (part == null) return NotFound();
            part.PartVehicles = await _db.PartVehicles.Where(pv => pv.PartId == id).ToListAsync();

            ViewBag.Vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToListAsync();

            return View(part); // Views/Admin/EditPart.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPart(Part part, int[]? vehicleIds, string[]? startYears, string[]? endYears, IFormFile? ImageFile, IFormFile[]? GalleryFiles)
        {
            part.VehicleId = null;

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
            {
                part.PartVehicles = await _db.PartVehicles.Where(pv => pv.PartId == part.Id).ToListAsync();
                return View(part);
            }

            var (uploadedPath, uploadError) = await SaveImageIfAny(ImageFile);
            if (!string.IsNullOrWhiteSpace(uploadError))
            {
                part.PartVehicles = await _db.PartVehicles.Where(pv => pv.PartId == part.Id).ToListAsync();
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
            return RedirectToAction("Parts");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePartImage(int id, int partId)
        {
            var img = await _db.PartImages.FirstOrDefaultAsync(pi => pi.Id == id && pi.PartId == partId);
            if (img != null)
            {
                _db.PartImages.Remove(img);
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(EditPart), new { id = partId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MovePartImage(int id, int partId, int direction)
        {
            var img = await _db.PartImages.FirstOrDefaultAsync(pi => pi.Id == id && pi.PartId == partId);
            if (img == null) return RedirectToAction(nameof(EditPart), new { id = partId });

            var images = await _db.PartImages
                .Where(pi => pi.PartId == partId)
                .OrderBy(pi => pi.SortOrder)
                .ToListAsync();

            var index = images.FindIndex(pi => pi.Id == id);
            if (index < 0) return RedirectToAction(nameof(EditPart), new { id = partId });

            var newIndex = index + (direction < 0 ? -1 : 1);
            if (newIndex < 0 || newIndex >= images.Count) return RedirectToAction(nameof(EditPart), new { id = partId });

            var temp = images[index].SortOrder;
            images[index].SortOrder = images[newIndex].SortOrder;
            images[newIndex].SortOrder = temp;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(EditPart), new { id = partId });
        }

        // =========================
        // VEHICLES - LIST (DB SEARCH)
        // =========================
        public async Task<IActionResult> Vehicles(string? q)
        {
            q = (q ?? "").Trim();

            var query = _db.Vehicles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";

                query = query.Where(v =>
                    EF.Functions.Like(v.Brand, like) ||
                    EF.Functions.Like(v.Model, like) ||
                    EF.Functions.Like(v.Engine, like) ||
                    EF.Functions.Like(v.Year.ToString(), like)
                );
            }

            var list = await query
                .OrderBy(v => v.Brand)
                .ThenBy(v => v.Model)
                .ThenBy(v => v.Year)
                .ToListAsync();

            ViewBag.Q = q;
            return View(list); // Views/Admin/Vehicles.cshtml
        }

        // =========================
        // VEHICLES - CSV IMPORT
        // =========================
        [HttpGet]
        public IActionResult ImportVehicles()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportVehicles(IFormFile csvFile, bool clearExisting = false)
        {
            if (csvFile == null || csvFile.Length == 0)
            {
                ModelState.AddModelError("", "CSV dosyasi secin.");
                return View();
            }

            var result = await ImportVehiclesFromCsv(csvFile, clearExisting);
            ViewBag.ImportResult = result;
            if (result.Errors.Count == 0)
            {
                ViewBag.ImportSuccess = $"Aktarim tamamlandi. Eklenen: {result.Imported}, Atlanan: {result.Skipped}.";
            }

            return View();
        }

        // =========================
        // VEHICLES - CREATE
        // =========================
        [HttpGet]
        public IActionResult CreateVehicle()
        {
            return View(); // Views/Admin/CreateVehicle.cshtml (varsa)
        }

        [HttpPost]
        public async Task<IActionResult> CreateVehicle(Vehicle vehicle)
        {
            NormalizeVehicleYears(vehicle);
            if (!ModelState.IsValid)
                return View(vehicle);

            _db.Vehicles.Add(vehicle);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Araç eklendi.";
            return RedirectToAction("Vehicles");
        }

        // =========================
        // VEHICLES - EDIT
        // =========================
        [HttpGet]
        public async Task<IActionResult> EditVehicle(int id)
        {
            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle == null) return NotFound();

            return View(vehicle); // Views/Admin/EditVehicle.cshtml
        }

        [HttpPost]
        public async Task<IActionResult> EditVehicle(Vehicle vehicle)
        {
            NormalizeVehicleYears(vehicle);
            if (!ModelState.IsValid)
                return View(vehicle);

            _db.Vehicles.Update(vehicle);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Araç güncellendi.";
            return RedirectToAction("Vehicles");
        }


        // =========================
        // PARTS - STOCK ADJUST (INLINE)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustPartStock(int id, int delta, string? q)
        {
            q = (q ?? "").Trim();

            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == id);
            if (part == null) return NotFound();

            var newStock = part.Stock + delta;
            if (newStock < 0) newStock = 0;

            part.Stock = newStock;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Stok güncellendi.";
            return RedirectToAction(nameof(Parts), new { q });
        }

        public async Task<IActionResult> Users(string? q)
        {
            q = (q ?? "").Trim();

            var usersQuery = _db.AppUsers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                usersQuery = usersQuery.Where(u =>
                    EF.Functions.Like(u.FullName, like) ||
                    EF.Functions.Like(u.Email, like));
            }

            var users = await usersQuery.OrderBy(u => u.FullName).ToListAsync();

            var pendingApps = await _db.SellerApplications
                .Include(a => a.User)
                .Where(a => a.Status == "Pending")
                .Where(a => string.IsNullOrWhiteSpace(q) ||
                    EF.Functions.Like(a.User.FullName, $"%{q}%") ||
                    EF.Functions.Like(a.User.Email, $"%{q}%") ||
                    EF.Functions.Like(a.CompanyName, $"%{q}%"))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();

            // Role'u SellerPending olan ama başvuru kaydı eksik kullanıcıları listeye ekle
            var pendingUsers = users.Where(u => u.Role == "SellerPending").ToList();
            var missingApps = new List<SellerApplication>();
            foreach (var pu in pendingUsers)
            {
                if (!pendingApps.Any(a => a.UserId == pu.Id))
                {
                    var app = new SellerApplication
                    {
                        UserId = pu.Id,
                        User = pu,
                        CompanyName = "(başvuru bekleniyor)",
                        ContactName = pu.FullName,
                        Phone = pu.Email,
                        Address = "",
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow
                    };
                    pendingApps.Add(app);
                    missingApps.Add(app);
                }
            }
            if (missingApps.Any())
            {
                _db.SellerApplications.AddRange(missingApps);
                await _db.SaveChangesAsync();
            }

            var sellerApps = await _db.SellerApplications
                .Include(a => a.User)
                .Where(a => a.Status == "Approved" || a.Status == "Suspended")
                .Where(a => string.IsNullOrWhiteSpace(q) ||
                    EF.Functions.Like(a.User.FullName, $"%{q}%") ||
                    EF.Functions.Like(a.User.Email, $"%{q}%") ||
                    EF.Functions.Like(a.CompanyName, $"%{q}%"))
                .OrderBy(a => a.CompanyName)
                .ToListAsync();

            var vm = new AdminUsersVm
            {
                Customers = users.Where(u => u.Role == "User").OrderBy(u => u.FullName).ToList(),
                Sellers = users.Where(u => u.Role == "Seller" || u.Role == "SellerSuspended").OrderBy(u => u.FullName).ToList(),
                PendingApplications = pendingApps,
                SellerApplications = sellerApps
            };

            ViewBag.Q = q;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserRole(int id, string role)
        {
            var allowed = new[] { "User", "Admin" };
            if (!allowed.Contains(role))
            {
                TempData["Warning"] = "Geçersiz rol.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            if (string.Equals(user.Email, DefaultAdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Warning"] = "Varsayılan admin rolü değiştirilemez.";
                return RedirectToAction(nameof(Users));
            }

            user.Role = role;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Rol güncellendi.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSeller(int id)
        {
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            user.Role = "Seller";
            var app = await _db.SellerApplications.FirstOrDefaultAsync(a => a.UserId == id);
            if (app != null)
            {
                app.Status = "Approved";
            }
            else
            {
                _db.SellerApplications.Add(new SellerApplication
                {
                    UserId = id,
                    CompanyName = "(onaylandı)",
                    ContactName = user.FullName,
                    Phone = user.Email,
                    Address = "",
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Satıcı onaylandı.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendSeller(int id)
        {
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();
            if (user.Role != "Seller")
            {
                TempData["Warning"] = "Sadece aktif satıcı askıya alınabilir.";
                return RedirectToAction(nameof(Users));
            }

            user.Role = "SellerSuspended";
            var app = await _db.SellerApplications.FirstOrDefaultAsync(a => a.UserId == id);
            if (app != null)
            {
                app.Status = "Suspended";
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Satıcı askıya alındı. Ürünler gizlendi.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReinstateSeller(int id)
        {
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();
            if (user.Role != "SellerSuspended")
            {
                TempData["Warning"] = "Askıya alınmış satıcı bulunamadı.";
                return RedirectToAction(nameof(Users));
            }

            user.Role = "Seller";
            var app = await _db.SellerApplications.FirstOrDefaultAsync(a => a.UserId == id);
            if (app != null)
            {
                app.Status = "Approved";
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Satıcı yeniden aktifleştirildi.";
            return RedirectToAction(nameof(Users));
        }

        public async Task<IActionResult> Complaints(string? status)
        {
            status = (status ?? "").Trim();
            var query = _db.ContactMessages.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.ToLower();
                query = query.Where(m => m.Status != null && m.Status.Trim().ToLower() == normalized);
            }

            var msgs = await query
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            ViewBag.FilterStatus = status;
            return View(msgs);
        }

        public async Task<IActionResult> ComplaintDetail(int id)
        {
            var msg = await _db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound();

            if ((msg.Status ?? "").Trim().Equals("Yeni", StringComparison.OrdinalIgnoreCase))
            {
                msg.Status = "Okundu";
                await _db.SaveChangesAsync();
            }

            return View(msg);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateComplaintStatus(int id, string status, string? returnUrl = null)
        {
            var msg = await _db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound();

            var allowed = new[] { "Yeni", "Okundu" };
            if (!allowed.Contains(status))
            {
                TempData["Warning"] = "Geçersiz durum.";
                return RedirectToLocal(returnUrl) ?? RedirectToAction(nameof(Complaints));
            }

            msg.Status = status;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Mesaj durumu güncellendi.";
            return RedirectToLocal(returnUrl) ?? RedirectToAction(nameof(Complaints));
        }

        // =========================
        // SELLER APPLICATION DETAIL
        // =========================
        [HttpGet]
        public async Task<IActionResult> SellerApplicationDetail(int id)
        {
            var app = await _db.SellerApplications
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (app == null) return NotFound();
            return View(app);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSellerApplicationNote(int id, string? note)
        {
            var app = await _db.SellerApplications.FirstOrDefaultAsync(a => a.Id == id);
            if (app == null) return NotFound();

            app.Note = note;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Başvuru notu güncellendi.";
            return RedirectToAction(nameof(SellerApplicationDetail), new { id });
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

            // eşleşen index ile yıl aralığı yakala
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

            var maxBytes = 2 * 1024 * 1024; // 2 MB
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

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        private void NormalizeVehicleYears(Vehicle vehicle)
        {
            if (vehicle.StartYear.HasValue || vehicle.EndYear.HasValue)
            {
                if (!vehicle.StartYear.HasValue || !vehicle.EndYear.HasValue)
                {
                    ModelState.AddModelError("", "Baslangic ve bitis yili birlikte girilmeli.");
                    return;
                }

                if (vehicle.StartYear.Value > vehicle.EndYear.Value)
                {
                    ModelState.AddModelError("", "Baslangic yili bitis yilindan buyuk olamaz.");
                    return;
                }

                vehicle.Year = vehicle.StartYear.Value;
                return;
            }

            if (vehicle.Year <= 0)
            {
                ModelState.AddModelError("Year", "Yil girin.");
            }
        }

        private sealed class VehicleImportResult
        {
            public int Imported { get; set; }
            public int Skipped { get; set; }
            public int DeletedParts { get; set; }
            public int DeletedVehicles { get; set; }
            public List<string> Errors { get; set; } = new();
        }

        private sealed class CsvColumnMap
        {
            public int Brand { get; set; } = -1;
            public int Model { get; set; } = -1;
            public int StartYear { get; set; } = -1;
            public int EndYear { get; set; } = -1;
            public int Year { get; set; } = -1;
            public int Engine { get; set; } = -1;
            public int ImageUrl { get; set; } = -1;
            public int BrandLogoUrl { get; set; } = -1;
        }

        private async Task<VehicleImportResult> ImportVehiclesFromCsv(IFormFile csvFile, bool clearExisting)
        {
            var result = new VehicleImportResult();

            using var stream = csvFile.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);

            var headerLine = await reader.ReadLineAsync();
            if (headerLine == null)
            {
                result.Errors.Add("CSV dosyasi bos.");
                return result;
            }

            var delimiter = DetectDelimiter(headerLine);
            var headers = ParseCsvLine(headerLine, delimiter);
            var map = BuildColumnMap(headers);

            if (map.Brand < 0 || map.Model < 0)
            {
                result.Errors.Add("CSV basliklari icinde Brand/Marka ve Model alanlari gerekli.");
                return result;
            }

            var vehicles = new List<Vehicle>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var row = 1;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                row++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = ParseCsvLine(line, delimiter);
                var brand = GetField(values, map.Brand);
                var model = GetField(values, map.Model);
                var engine = GetField(values, map.Engine);
                var imageUrl = GetField(values, map.ImageUrl);
                var brandLogoUrl = GetField(values, map.BrandLogoUrl);

                if (string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(model))
                {
                    AddRowError(result, row, "Marka veya model bos.");
                    continue;
                }

                var startText = GetField(values, map.StartYear);
                var endText = GetField(values, map.EndYear);
                var yearText = GetField(values, map.Year);

                if (string.IsNullOrWhiteSpace(startText) && !string.IsNullOrWhiteSpace(yearText)) startText = yearText;
                if (string.IsNullOrWhiteSpace(endText) && !string.IsNullOrWhiteSpace(yearText)) endText = yearText;

                if (!TryParseYear(startText, out var startYear) || !TryParseYear(endText, out var endYear))
                {
                    AddRowError(result, row, "Baslangic/bitis yili gecersiz.");
                    continue;
                }

                if (startYear > endYear)
                {
                    (startYear, endYear) = (endYear, startYear);
                }

                var key = $"{brand}|{model}|{startYear}|{endYear}|{engine}".Trim();
                if (!seen.Add(key))
                {
                    result.Skipped++;
                    continue;
                }

                vehicles.Add(new Vehicle
                {
                    Brand = brand,
                    Model = model,
                    StartYear = startYear,
                    EndYear = endYear,
                    Year = startYear,
                    Engine = string.IsNullOrWhiteSpace(engine) ? null : engine,
                    ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                    BrandLogoUrl = string.IsNullOrWhiteSpace(brandLogoUrl) ? null : brandLogoUrl
                });
            }

            if (vehicles.Count == 0)
            {
                result.Errors.Add("Aktarilacak kayit bulunamadi.");
                return result;
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            if (clearExisting)
            {
                await _db.PartVehicles.ExecuteDeleteAsync();
                await _db.Database.ExecuteSqlRawAsync("UPDATE Parts SET VehicleId = NULL;");
                result.DeletedVehicles = await _db.Vehicles.ExecuteDeleteAsync();
            }

            _db.Vehicles.AddRange(vehicles);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            result.Imported = vehicles.Count;
            return result;
        }

        private static void AddRowError(VehicleImportResult result, int row, string message)
        {
            if (result.Errors.Count >= 20) return;
            result.Errors.Add($"Satir {row}: {message}");
        }

        private static bool TryParseYear(string? value, out int year)
        {
            if (int.TryParse(value, out year))
            {
                if (year > 0) return true;
            }
            year = 0;
            return false;
        }

        private static string GetField(List<string> values, int index)
        {
            if (index < 0 || index >= values.Count) return "";
            return (values[index] ?? "").Trim();
        }

        private static char DetectDelimiter(string line)
        {
            var comma = line.Count(c => c == ',');
            var semi = line.Count(c => c == ';');
            var tab = line.Count(c => c == '\t');

            if (semi >= comma && semi >= tab) return ';';
            if (tab >= comma) return '\t';
            return ',';
        }

        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            if (line == null) return result;

            var sb = new StringBuilder();
            var inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        sb.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString());
            return result;
        }

        private static CsvColumnMap BuildColumnMap(List<string> headers)
        {
            var map = new CsvColumnMap();
            var normalized = headers.Select(NormalizeHeader).ToList();

            map.Brand = FindIndex(normalized, new[] { "brand", "marka" });
            map.Model = FindIndex(normalized, new[] { "model", "modeladi", "modelad" });
            map.StartYear = FindIndex(normalized, new[] { "startyear", "baslangicyil", "baslangicyili", "yilbaslangic", "yearstart" });
            map.EndYear = FindIndex(normalized, new[] { "endyear", "bitisyil", "bitisyili", "yilbitis", "yearend" });
            map.Year = FindIndex(normalized, new[] { "year", "yil", "modelyili" });
            map.Engine = FindIndex(normalized, new[] { "engine", "motor", "variant" });
            map.ImageUrl = FindIndex(normalized, new[] { "imageurl", "image", "img", "gorsel", "gorselurl", "resim", "resimurl", "gorseladres", "gorseladresi" });
            map.BrandLogoUrl = FindIndex(normalized, new[] { "brandlogourl", "brandlogo", "markalogosu", "markalogo", "markalogourl", "markalogoURL", "brandlogoURL" });

            return map;
        }

        private static int FindIndex(List<string> headers, IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                var idx = headers.FindIndex(h => h == candidate);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        private static string NormalizeHeader(string value)
        {
            var text = (value ?? "").Trim().ToLowerInvariant();
            text = text.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "");
            text = text
                .Replace("\u00e7", "c")
                .Replace("\u011f", "g")
                .Replace("\u0131", "i")
                .Replace("\u00f6", "o")
                .Replace("\u015f", "s")
                .Replace("\u00fc", "u");
            return text;
        }
    }
}
