using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using AutoPartsWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    public class SellerApplyController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailSender;

        public SellerApplyController(ApplicationDbContext db, IEmailSender emailSender)
        {
            _db = db;
            _emailSender = emailSender;
        }

        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.CurrentEmail = User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            ViewBag.CurrentName = User?.Identity?.Name ?? "";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(string fullName, string email, string? password, string companyName, string phone, string address, string? taxNumber, string? note)
        {
            fullName = (fullName ?? "").Trim();
            email = (email ?? "").Trim();
            companyName = (companyName ?? "").Trim();
            phone = (phone ?? "").Trim();
            address = (address ?? "").Trim();

            var currentEmail = User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                               ?? User?.Identity?.Name;
            var isLoggedIn = User?.Identity?.IsAuthenticated ?? false;
            if (isLoggedIn && string.IsNullOrWhiteSpace(email))
                email = currentEmail ?? "";
            if (isLoggedIn && string.IsNullOrWhiteSpace(fullName))
                fullName = User?.Identity?.Name ?? "";

            if (string.IsNullOrWhiteSpace(fullName))
                ModelState.AddModelError(nameof(fullName), "Ad soyad zorunlu.");
            if (string.IsNullOrWhiteSpace(email))
                ModelState.AddModelError(nameof(email), "E-posta zorunlu.");
            else if (!ValidationRules.IsValidEmail(email))
                ModelState.AddModelError(nameof(email), "Gecerli bir e-posta girin.");
            if (string.IsNullOrWhiteSpace(companyName))
                ModelState.AddModelError(nameof(companyName), "Firma adi zorunlu.");
            if (string.IsNullOrWhiteSpace(phone))
                ModelState.AddModelError(nameof(phone), "Telefon zorunlu.");
            if (string.IsNullOrWhiteSpace(address))
                ModelState.AddModelError(nameof(address), "Adres zorunlu.");

            var existingUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (existingUser == null)
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    ModelState.AddModelError(nameof(password), "Sifre zorunlu.");
                }
                else if (!ValidationRules.TryValidatePassword(password, out var passwordError))
                {
                    ModelState.AddModelError(nameof(password), passwordError);
                }
            }

            if (!ModelState.IsValid)
                return View();

            AppUser user;
            if (existingUser == null)
            {
                var confirmToken = TokenGenerator.CreateToken();
                user = new AppUser
                {
                    FullName = fullName,
                    Email = email,
                    PasswordHash = PasswordHasher.Hash(password ?? ""),
                    Role = "SellerPending",
                    EmailConfirmed = false,
                    EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken),
                    EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24)
                };
                _db.AppUsers.Add(user);
                await _db.SaveChangesAsync();

                var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email, token = confirmToken }, Request.Scheme);
                if (!string.IsNullOrWhiteSpace(confirmLink))
                {
                    var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                    await _emailSender.SendAsync(email, "E-posta Onayi", body);
                }
            }
            else
            {
                user = existingUser;
                if (user.Role == "Seller")
                {
                    TempData["Success"] = "Zaten satici olarak tanimlisiniz.";
                    return RedirectToAction(nameof(Thanks));
                }
                if (!string.IsNullOrWhiteSpace(fullName))
                    user.FullName = fullName;
                user.Role = "SellerPending";

                if (!user.EmailConfirmed)
                {
                    var confirmToken = TokenGenerator.CreateToken();
                    user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
                    user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);
                    await _db.SaveChangesAsync();

                    var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email, token = confirmToken }, Request.Scheme);
                    if (!string.IsNullOrWhiteSpace(confirmLink))
                    {
                        var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                        await _emailSender.SendAsync(email, "E-posta Onayi", body);
                    }
                }
                else
                {
                    await _db.SaveChangesAsync();
                }
            }

            var existingApp = await _db.SellerApplications.FirstOrDefaultAsync(a => a.UserId == user.Id);
            if (existingApp != null)
            {
                existingApp.CompanyName = companyName;
                existingApp.ContactName = fullName;
                existingApp.Phone = phone;
                existingApp.Address = address;
                existingApp.TaxNumber = taxNumber;
                existingApp.Note = note;
                existingApp.Status = "Pending";
                existingApp.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.SellerApplications.Add(new SellerApplication
                {
                    UserId = user.Id,
                    CompanyName = companyName,
                    ContactName = fullName,
                    Phone = phone,
                    Address = address,
                    TaxNumber = taxNumber,
                    Note = note,
                    Status = "Pending"
                });
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = "Basvurunuz alindi. Admin onayindan sonra satici paneli acilacaktir.";
            return RedirectToAction(nameof(Thanks));
        }

        [HttpGet]
        public IActionResult Thanks()
        {
            return View();
        }
    }
}
