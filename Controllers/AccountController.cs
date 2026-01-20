using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using AutoPartsWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AutoPartsWeb.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailSender;

        public AccountController(ApplicationDbContext db, IEmailSender emailSender)
        {
            _db = db;
            _emailSender = emailSender;
        }

        public async Task<IActionResult> Profile()
        {
            AppUser? user = null;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out var userId))
            {
                user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            }

            if (user == null)
            {
                var email = User?.Identity?.Name;
                user = await _db.AppUsers.FirstOrDefaultAsync(u => u.FullName == email || u.Email == email);
            }

            if (user == null)
                return RedirectToAction("Login", "Auth");

            return View(user);
        }

        [Authorize(Roles = "User,SellerPending")]
        public async Task<IActionResult> MyOrders(string? status)
        {
            status = (status ?? "").Trim();
            var showingCompleted = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
            var showingCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            var email = User?.Identity?.Name;
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.FullName == email || u.Email == email);
            if (user == null) return RedirectToAction("Login", "Auth");

            IQueryable<Order> query = _db.Orders
                .Where(o => o.Email == user.Email);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);
            else
                query = query.Where(o => o.Status != "Completed" && o.Status != "Cancelled");

            var orders = await query
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.FilterStatus = status;
            ViewBag.ShowingCompleted = showingCompleted;
            ViewBag.ShowingCancelled = showingCancelled;
            return View(orders);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User,SellerPending")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var email = User?.Identity?.Name;
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.FullName == email || u.Email == email);
            if (user == null) return RedirectToAction("Login", "Auth");

            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id && o.Email == user.Email);
            if (order == null) return NotFound();

            if (order.Status == "Cancelled" || order.Status == "Completed")
            {
                TempData["Warning"] = "Bu sipariş iptal edilemez.";
                return RedirectToAction(nameof(MyOrders));
            }

            // Stok iadesi
            var partIds = order.Items.Select(i => i.PartId).Distinct().ToList();
            var parts = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync();
            foreach (var item in order.Items)
            {
                var part = parts.FirstOrDefault(p => p.Id == item.PartId);
                if (part != null)
                {
                    part.Stock += item.Quantity;
                }
            }

            order.Status = "Cancelled";
            await _db.SaveChangesAsync();

            TempData["Success"] = "Sipariş iptal edildi ve stoklar geri alındı.";
            return RedirectToAction(nameof(MyOrders));
        }

        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            AppUser? user = null;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out var userId))
            {
                user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            }

            if (user == null)
            {
                var email = User?.Identity?.Name;
                user = await _db.AppUsers.FirstOrDefaultAsync(u => u.FullName == email || u.Email == email);
            }

            if (user == null)
                return RedirectToAction("Login", "Auth");

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(string fullName, string email)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Login", "Auth");

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return RedirectToAction("Login", "Auth");

            if (string.IsNullOrWhiteSpace(fullName))
            {
                ModelState.AddModelError("", "Ad soyad gerekli.");
                return View(user);
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ModelState.AddModelError("", "Geçerli bir e-posta girin.");
                return View(user);
            }

            email = email.Trim();
            fullName = fullName.Trim();

            // Email değiştirildiyse ve başka bir kullanıcıda varsa hata
            if (email != user.Email)
            {
                var emailExists = await _db.AppUsers.AnyAsync(u => u.Email == email && u.Id != userId);
                if (emailExists)
                {
                    ModelState.AddModelError("", "Bu e-posta zaten kullanılıyor.");
                    return View(user);
                }

                // Email değiştiyse e-posta onayını sıfırla
                user.EmailConfirmed = false;
                var confirmToken = TokenGenerator.CreateToken();
                user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
                user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);

                var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email, token = confirmToken }, Request.Scheme);
                if (!string.IsNullOrWhiteSpace(confirmLink))
                {
                    var body = $"E-posta onay linkiniz: {confirmLink}\nBu bağlantı 24 saat geçerlidir.";
                    await _emailSender.SendAsync(email, "E-posta Onayı", body);
                }
            }

            var emailChanged = email != user.Email;
            user.FullName = fullName;
            user.Email = email;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Profil güncellendi.";
            if (emailChanged)
            {
                TempData["Info"] = "E-posta değiştiği için e-posta onayı gerekli. Yeni e-posta adresinize onay linki gönderildi.";
            }

            return RedirectToAction(nameof(Profile));
        }
    }
}
