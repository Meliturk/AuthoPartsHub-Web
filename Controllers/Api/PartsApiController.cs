using System.Security.Claims;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers.Api
{
    [ApiController]
    [Route("api/parts")]
    public class PartsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private const string AuthSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}";

        public PartsApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PartListDto>>> GetParts(
            [FromQuery] string? q,
            [FromQuery] string? category,
            [FromQuery] string[]? categoryList,
            [FromQuery] int? vehicleId,
            [FromQuery] string? brand,
            [FromQuery] string[]? brandList,
            [FromQuery] string? model,
            [FromQuery] string[]? modelList,
            [FromQuery] int? year,
            [FromQuery] int[]? yearList,
            [FromQuery] string? partBrand,
            [FromQuery] string[]? partBrandList,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] string? sort)
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
            if (brandList != null)
            {
                foreach (var b in brandList.Where(x => !string.IsNullOrWhiteSpace(x)))
                    selectedBrands.Add(b.Trim());
            }
            if (!string.IsNullOrWhiteSpace(brand))
                selectedBrands.Add(brand);

            var selectedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (modelList != null)
            {
                foreach (var m in modelList.Where(x => !string.IsNullOrWhiteSpace(x)))
                    selectedModels.Add(m.Trim());
            }
            if (!string.IsNullOrWhiteSpace(model))
                selectedModels.Add(model);

            var selectedYears = new HashSet<int>();
            if (yearList != null)
            {
                foreach (var y in yearList)
                {
                    if (y > 0) selectedYears.Add(y);
                }
            }
            if (year.HasValue && year.Value > 0)
                selectedYears.Add(year.Value);

            var queryable = _db.Parts
                .Include(p => p.Seller)
                .Include(p => p.Vehicle)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                queryable = queryable.Where(p =>
                    EF.Functions.Like(p.Name, like) ||
                    EF.Functions.Like(p.Brand, like) ||
                    EF.Functions.Like(p.Category, like)
                );
            }

            if (selectedCategories.Count > 0)
                queryable = queryable.Where(p => selectedCategories.Contains(p.Category));

            if (selectedPartBrands.Count > 0)
                queryable = queryable.Where(p => selectedPartBrands.Contains(p.Brand));

            if (vehicleId.HasValue && vehicleId > 0)
            {
                queryable = queryable.Where(p =>
                    p.PartVehicles.Any(pv => pv.VehicleId == vehicleId.Value) ||
                    (p.VehicleId != null && p.VehicleId == vehicleId.Value));
            }
            else if (selectedBrands.Count > 0 || selectedModels.Count > 0 || selectedYears.Count > 0)
            {
                var vehiclesQuery = _db.Vehicles.AsQueryable();
                if (selectedBrands.Count > 0)
                    vehiclesQuery = vehiclesQuery.Where(v => selectedBrands.Contains(v.Brand));
                if (selectedModels.Count > 0)
                    vehiclesQuery = vehiclesQuery.Where(v => selectedModels.Contains(v.Model));
                if (selectedYears.Count > 0)
                {
                    vehiclesQuery = vehiclesQuery.Where(v =>
                        (v.StartYear.HasValue && v.EndYear.HasValue && selectedYears.Any(y => v.StartYear.Value <= y && v.EndYear.Value >= y)) ||
                        (!v.StartYear.HasValue && !v.EndYear.HasValue && selectedYears.Contains(v.Year)) ||
                        (v.StartYear.HasValue && !v.EndYear.HasValue && selectedYears.Contains(v.StartYear.Value)) ||
                        (!v.StartYear.HasValue && v.EndYear.HasValue && selectedYears.Contains(v.EndYear.Value)));
                }

                var matchedIds = await vehiclesQuery.Select(v => v.Id).ToListAsync();
                if (matchedIds.Count > 0)
                {
                    queryable = queryable.Where(p =>
                        p.PartVehicles.Any(pv => matchedIds.Contains(pv.VehicleId)) ||
                        (p.VehicleId != null && matchedIds.Contains(p.VehicleId.Value)));
                }
                else
                {
                    queryable = queryable.Where(p => false);
                }
            }

            if (minPrice.HasValue) queryable = queryable.Where(p => p.Price >= minPrice.Value);
            if (maxPrice.HasValue) queryable = queryable.Where(p => p.Price <= maxPrice.Value);

            queryable = sort switch
            {
                "price_asc" => queryable.OrderBy(p => p.Price),
                "price_desc" => queryable.OrderByDescending(p => p.Price),
                "name_asc" => queryable.OrderBy(p => p.Name),
                "name_desc" => queryable.OrderByDescending(p => p.Name),
                _ => queryable.OrderByDescending(p => p.Id)
            };

            var parts = await queryable.ToListAsync();
            var partIds = parts.Select(p => p.Id).ToList();
            var ratingDict = await _db.ProductReviews
                .Where(r => partIds.Contains(r.PartId))
                .GroupBy(r => r.PartId)
                .Select(g => new { g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

            var response = parts.Select(p =>
            {
                var vehicles = GetVehicles(p);
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
                    vehicles,
                    p.Seller?.FullName);
            }).ToList();

            return Ok(response);
        }

        [HttpGet("filters")]
        public async Task<ActionResult<PartsFilterMetaDto>> GetFilters()
        {
            var categories = await _db.Parts
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var partBrands = await _db.Parts
                .Select(p => p.Brand)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct()
                .OrderBy(b => b)
                .ToListAsync();

            var vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand)
                .ThenBy(v => v.Model)
                .ThenBy(v => v.Year)
                .ToListAsync();

            var vehicleDtos = vehicles
                .Select(v => new VehicleDto(v.Id, v.Brand, v.Model, v.Year, v.Engine, v.StartYear, v.EndYear, v.ImageUrl, v.BrandLogoUrl))
                .ToList();

            var prices = await _db.Parts.Select(p => p.Price).ToListAsync();
            var minPrice = prices.Count == 0 ? 0m : prices.Min();
            var maxPrice = prices.Count == 0 ? 0m : prices.Max();

            return Ok(new PartsFilterMetaDto(categories, partBrands, vehicleDtos, minPrice, maxPrice));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PartDetailDto>> GetPart(int id)
        {
            var part = await _db.Parts
                .Include(p => p.Seller)
                .Include(p => p.Vehicle)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .Include(p => p.PartImages)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (part == null) return NotFound();

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

            var dto = new PartDetailDto(
                part.Id,
                part.Name,
                part.Brand,
                part.Category,
                part.Price,
                part.Stock,
                part.Description,
                part.ImageUrl,
                part.SellerId,
                part.Seller?.FullName,
                GetVehicles(part),
                part.PartImages.OrderBy(pi => pi.SortOrder).Select(pi => new PartImageDto(pi.Id, pi.Url, pi.SortOrder)).ToList(),
                avg,
                reviews.Count,
                questions.Select(q => new QuestionDto(q.Id, q.Question, q.Answer, q.User?.FullName, q.CreatedAt, q.AnsweredAt)).ToList(),
                reviews.Select(r => new ReviewDto(r.Id, r.Rating, r.Comment, r.User?.FullName, r.CreatedAt)).ToList()
            );

            return Ok(dto);
        }

        [HttpPost("{id}/questions")]
        [Authorize(AuthenticationSchemes = AuthSchemes)]
        public async Task<IActionResult> AskQuestion(int id, [FromBody] QuestionCreateRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var partExists = await _db.Parts.AnyAsync(p => p.Id == id);
            if (!partExists) return NotFound();

            var userId = GetUserId();
            _db.ProductQuestions.Add(new ProductQuestion
            {
                PartId = id,
                UserId = userId,
                Question = (req.Question ?? "").Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Soru kaydedildi." });
        }

        [HttpPost("{id}/reviews")]
        [Authorize(AuthenticationSchemes = AuthSchemes)]
        public async Task<IActionResult> AddReview(int id, [FromBody] ReviewCreateRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var partExists = await _db.Parts.AnyAsync(p => p.Id == id);
            if (!partExists) return NotFound();

            var userId = GetUserId();
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { message = "Kullanıcı e-postası bulunamadı." });

            var purchased = await _db.Orders
                .Include(o => o.Items)
                .AnyAsync(o =>
                    o.Status == "Completed" &&
                    o.Email == userEmail &&
                    o.Items.Any(i => i.PartId == id));

            if (!purchased)
                return Forbid($"Değerlendirme için ürünü satın almış olmanız gerekir.");

            if (userId.HasValue)
            {
                var already = await _db.ProductReviews.AnyAsync(r => r.PartId == id && r.UserId == userId.Value);
                if (already)
                    return Conflict(new { message = "Bu ürüne zaten değerlendirme yaptınız." });
            }

            _db.ProductReviews.Add(new ProductReview
            {
                PartId = id,
                UserId = userId,
                Rating = Math.Clamp(req.Rating, 1, 5),
                Comment = (req.Comment ?? "").Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Değerlendirmeniz kaydedildi." });
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

        private int? GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out var uid)) return uid;
            return null;
        }
    }
}
