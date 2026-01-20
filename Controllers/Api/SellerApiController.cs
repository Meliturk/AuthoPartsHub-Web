using System.Linq;
using System.Security.Claims;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers.Api
{
    [ApiController]
    [Route("api/seller")]
    [Authorize(AuthenticationSchemes = AuthSchemes, Roles = "Seller")]
    public class SellerApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private const string AuthSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}";

        public SellerApiController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private int CurrentUserId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        [HttpGet("dashboard")]
        public async Task<ActionResult<object>> Dashboard()
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

            decimal SalesSum(Func<Order, bool> filter) =>
                salesOrders.Where(filter)
                      .SelectMany(o => o.Items.Where(i => i.Part.SellerId == sellerId))
                      .Sum(i => i.UnitPrice * i.Quantity);

            var last7 = Enumerable.Range(0, 7)
                .Select(offset => now.Date.AddDays(-offset))
                .OrderBy(d => d)
                .ToList();

            var chartPoints = last7.Select(day => new
            {
                label = day.ToString("dd.MM"),
                total = (double)salesOrders
                    .Where(o => o.CreatedAt.Date == day.Date)
                    .SelectMany(o => o.Items.Where(i => i.Part.SellerId == sellerId))
                    .Sum(i => i.UnitPrice * i.Quantity)
            }).ToList();

            var recentItems = salesOrders
                .Where(o => o.CreatedAt >= now.AddDays(-30))
                .SelectMany(o => o.Items.Where(i => i.Part != null && i.Part.SellerId == sellerId))
                .ToList();

            var productSales = recentItems
                .GroupBy(i => new { i.PartId, Name = i.Part?.Name ?? $"Part #{i.PartId}" })
                .Select(g => new
                {
                    partId = g.Key.PartId,
                    name = g.Key.Name,
                    quantity = g.Sum(x => x.Quantity),
                    total = (double)g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .OrderByDescending(x => x.total)
                .Take(7)
                .ToList();

            var stats = new
            {
                dayTotal = SalesSum(o => o.CreatedAt >= now.AddDays(-1)),
                weekTotal = SalesSum(o => o.CreatedAt >= now.AddDays(-7)),
                monthTotal = SalesSum(o => o.CreatedAt >= now.AddMonths(-1)),
                partsCount = await _db.Parts.CountAsync(p => p.SellerId == sellerId),
                ordersCount = salesOrders.Count,
                chartPoints = chartPoints,
                productSales = productSales
            };

            return Ok(stats);
        }

        [HttpGet("parts")]
        public async Task<ActionResult<IEnumerable<PartListDto>>> Parts()
        {
            var sellerId = CurrentUserId;
            var parts = await _db.Parts
                .Include(p => p.Seller)
                .Include(p => p.Vehicle)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .Where(p => p.SellerId == sellerId)
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            var partIds = parts.Select(p => p.Id).ToList();
            var ratingDict = await _db.ProductReviews
                .Where(r => partIds.Contains(r.PartId))
                .GroupBy(r => r.PartId)
                .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

            var list = parts.Select(p =>
            {
                ratingDict.TryGetValue(p.Id, out var r);
                var rating = r == default ? (0, 0) : (r.Avg, r.Count);
                return new PartListDto(
                    p.Id,
                    p.Name,
                    p.Brand,
                    p.Category,
                    p.Price,
                    p.Stock,
                    p.ImageUrl,
                    rating.Item1,
                    rating.Item2,
                    GetVehicles(p),
                    p.Seller?.FullName);
            }).ToList();

            return Ok(list);
        }

        [HttpPost("parts")]
        public async Task<IActionResult> CreatePart([FromForm] PartCreateRequest req, IFormFile? image, [FromForm] IFormFile[]? gallery)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var validIds = await _db.Vehicles.Select(v => v.Id).ToListAsync();
            if (req.VehicleIds != null)
            {
                foreach (var vid in req.VehicleIds)
                {
                    if (!validIds.Contains(vid))
                        ModelState.AddModelError("VehicleIds", "Seçilen araç bulunamadı.");
                }
            }

            if (!ModelState.IsValid) return BadRequest(ModelState);

            var part = new Part
            {
                Name = req.Name,
                Brand = req.Brand,
                Category = req.Category,
                Price = req.Price,
                Stock = req.Stock,
                Description = req.Description,
                SellerId = CurrentUserId
            };

            var (imgPath, imgErr) = await SaveImageIfAny(image);
            if (!string.IsNullOrWhiteSpace(imgErr))
                return BadRequest(new { message = imgErr });
            if (!string.IsNullOrWhiteSpace(imgPath))
                part.ImageUrl = imgPath;

            _db.Parts.Add(part);
            await _db.SaveChangesAsync();

            await SyncPartVehicles(part.Id, req.VehicleIds);

            if (gallery != null && gallery.Length > 0)
            {
                var currentMax = await _db.PartImages.Where(pi => pi.PartId == part.Id).Select(pi => (int?)pi.SortOrder).MaxAsync() ?? 0;
                foreach (var gf in gallery)
                {
                    var (gPath, gErr) = await SaveImageIfAny(gf);
                    if (!string.IsNullOrWhiteSpace(gErr)) continue;
                    if (!string.IsNullOrWhiteSpace(gPath))
                    {
                        currentMax++;
                        _db.PartImages.Add(new PartImage { PartId = part.Id, Url = gPath, SortOrder = currentMax });
                    }
                }
                await _db.SaveChangesAsync();
            }

            return Ok(new { partId = part.Id });
        }

        [HttpPut("parts/{id}")]
        public async Task<IActionResult> UpdatePart(int id, [FromForm] PartCreateRequest req, IFormFile? image, [FromForm] IFormFile[]? gallery)
        {
            var part = await _db.Parts
                .Include(p => p.PartImages)
                .FirstOrDefaultAsync(p => p.Id == id && p.SellerId == CurrentUserId);
            if (part == null) return NotFound();

            if (!ModelState.IsValid) return BadRequest(ModelState);

            var validIds = await _db.Vehicles.Select(v => v.Id).ToListAsync();
            if (req.VehicleIds != null)
            {
                foreach (var vid in req.VehicleIds)
                {
                    if (!validIds.Contains(vid))
                        ModelState.AddModelError("VehicleIds", "Seçilen araç bulunamadı.");
                }
            }

            if (!ModelState.IsValid) return BadRequest(ModelState);

            part.Name = req.Name;
            part.Brand = req.Brand;
            part.Category = req.Category;
            part.Price = req.Price;
            part.Stock = req.Stock;
            part.Description = req.Description;

            var (imgPath, imgErr) = await SaveImageIfAny(image);
            if (!string.IsNullOrWhiteSpace(imgErr))
                return BadRequest(new { message = imgErr });
            if (!string.IsNullOrWhiteSpace(imgPath))
                part.ImageUrl = imgPath;

            await SyncPartVehicles(part.Id, req.VehicleIds);

            if (gallery != null && gallery.Length > 0)
            {
                var currentMax = await _db.PartImages.Where(pi => pi.PartId == part.Id).Select(pi => (int?)pi.SortOrder).MaxAsync() ?? 0;
                foreach (var gf in gallery)
                {
                    var (gPath, gErr) = await SaveImageIfAny(gf);
                    if (!string.IsNullOrWhiteSpace(gErr)) continue;
                    if (!string.IsNullOrWhiteSpace(gPath))
                    {
                        currentMax++;
                        _db.PartImages.Add(new PartImage { PartId = part.Id, Url = gPath, SortOrder = currentMax });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Parça güncellendi." });
        }

        [HttpGet("questions")]
        public async Task<ActionResult<IEnumerable<QuestionDto>>> Questions()
        {
            var sellerId = CurrentUserId;
            var qs = await _db.ProductQuestions
                .Include(q => q.Part)
                .Include(q => q.User)
                .Where(q => q.Part.SellerId == sellerId)
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();

            var dto = qs.Select(q => new QuestionDto(q.Id, q.Question, q.Answer, q.User?.FullName, q.CreatedAt, q.AnsweredAt)).ToList();
            return Ok(dto);
        }

        [HttpPost("questions/{id}/answer")]
        public async Task<IActionResult> AnswerQuestion(int id, [FromBody] AnswerQuestionRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var sellerId = CurrentUserId;
            var q = await _db.ProductQuestions
                .Include(x => x.Part)
                .FirstOrDefaultAsync(x => x.Id == id && x.Part.SellerId == sellerId);
            if (q == null) return NotFound();

            q.Answer = (req.Answer ?? "").Trim();
            q.AnsweredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Yanıt kaydedildi." });
        }

        [HttpGet("orders")]
        public async Task<ActionResult<IEnumerable<SellerOrderDto>>> Orders()
        {
            var sellerId = CurrentUserId;
            var orders = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .Where(o => o.Items.Any(i => i.Part.SellerId == sellerId))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var list = orders.Select(o =>
            {
                var items = o.Items.Where(i => i.Part.SellerId == sellerId)
                    .Select(i => new OrderItemDto(
                        i.PartId,
                        i.Part?.Name ?? $"Part #{i.PartId}",
                        i.Quantity,
                        i.UnitPrice,
                        i.Part?.ImageUrl))
                    .ToList();

                return new SellerOrderDto(o.Id, o.CreatedAt, o.Status, o.CustomerName, items.Sum(x => x.UnitPrice * x.Quantity), items);
            }).ToList();

            return Ok(list);
        }

        [HttpPost("orders/{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromForm] string status)
        {
            var sellerId = CurrentUserId;
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            if (!order.Items.Any(i => i.Part.SellerId == sellerId)) return Forbid();

            var allowed = new[] { "Pending", "Processing", "Shipped", "Cancelled", "Completed" };
            if (!allowed.Contains(status))
                return BadRequest(new { message = "Geçersiz durum." });

            order.Status = status;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Sipariş durumu güncellendi." });
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

        private static List<VehicleDto> GetVehicles(Part part)
        {
            var vehicles = new List<VehicleDto>();
            if (part.Vehicle != null)
            {
                vehicles.Add(new VehicleDto(
                    part.Vehicle.Id,
                    part.Vehicle.Brand,
                    part.Vehicle.Model,
                    part.Vehicle.Year,
                    part.Vehicle.Engine,
                    part.Vehicle.StartYear,
                    part.Vehicle.EndYear,
                    part.Vehicle.ImageUrl,
                    part.Vehicle.BrandLogoUrl));
            }

            if (part.PartVehicles != null)
            {
                foreach (var pv in part.PartVehicles)
                {
                    if (pv.Vehicle == null) continue;
                    if (vehicles.Any(v => v.Id == pv.VehicleId)) continue;
                    vehicles.Add(new VehicleDto(
                        pv.Vehicle.Id,
                        pv.Vehicle.Brand,
                        pv.Vehicle.Model,
                        pv.Vehicle.Year,
                        pv.Vehicle.Engine,
                        pv.Vehicle.StartYear,
                        pv.Vehicle.EndYear,
                        pv.Vehicle.ImageUrl,
                        pv.Vehicle.BrandLogoUrl));
                }
            }

            return vehicles;
        }
    }
}
