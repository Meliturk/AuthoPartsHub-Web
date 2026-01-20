using System.Security.Claims;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using AutoPartsWeb.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _env;

        public AuthController(ApplicationDbContext db, IEmailSender emailSender, IWebHostEnvironment env)
        {
            _db = db;
            _emailSender = emailSender;
            _env = env;
        }

        // GET: /Auth/Login
        public IActionResult Login() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError(string.Empty, "E-posta ve sifre zorunlu.");
                return View();
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email.Trim());
            if (user == null || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "E-posta veya sifre hatali.");
                return View();
            }

            if (!user.EmailConfirmed)
            {
                var confirmLink = await SendConfirmationEmailAsync(user);
                if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
                {
                    ViewBag.ConfirmLink = confirmLink;
                }
                ModelState.AddModelError(string.Empty, "E-posta dogrulanmamis. Onay linki yeniden gonderildi.");
                return View();
            }

            await SignIn(user);
            return RedirectToLocal(returnUrl) ?? RedirectToAction("Index", "Parts");
        }

        // GET: /Auth/Register
        public IActionResult Register() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string fullName, string email, string password, string? returnUrl = null)
        {
            fullName = (fullName ?? "").Trim();
            email = (email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(fullName))
                ModelState.AddModelError(nameof(fullName), "Ad Soyad zorunlu.");

            if (string.IsNullOrWhiteSpace(email))
                ModelState.AddModelError(nameof(email), "E-posta zorunlu.");
            else if (!ValidationRules.IsValidEmail(email))
                ModelState.AddModelError(nameof(email), "Gecerli bir e-posta girin.");

            if (!ValidationRules.TryValidatePassword(password, out var passwordError))
                ModelState.AddModelError(nameof(password), passwordError);

            var exists = await _db.AppUsers.AnyAsync(u => u.Email == email);
            if (exists)
                ModelState.AddModelError(nameof(email), "Bu e-posta zaten kayitli.");

            if (!ModelState.IsValid)
                return View();

            var user = new AppUser
            {
                FullName = fullName,
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = "User",
                EmailConfirmed = false
            };

            var confirmToken = TokenGenerator.CreateToken();
            user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
            user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            var confirmLink = await SendConfirmationEmailAsync(user, confirmToken);
            if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
            {
                TempData["ConfirmLink"] = confirmLink;
            }
            TempData["Success"] = "E-posta onay linki gonderildi. Lutfen e-postanizi kontrol edin.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            email = (email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
            {
                ModelState.AddModelError(nameof(email), "Gecerli bir e-posta girin.");
                return View();
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user != null)
            {
                var token = TokenGenerator.CreateToken();
                user.PasswordResetTokenHash = PasswordHasher.Hash(token);
                user.PasswordResetExpiresAt = DateTime.UtcNow.AddHours(1);
                await _db.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "Auth", new { email, token }, Request.Scheme);
                if (!string.IsNullOrWhiteSpace(resetLink))
                {
                    var body = $"Sifre sifirlama baglantiniz: {resetLink}\nBu baglanti 1 saat gecerlidir.";
                    await _emailSender.SendAsync(email, "Sifre Sifirlama", body);
                    if (_env.IsDevelopment())
                    {
                        TempData["ResetLink"] = resetLink;
                    }
                }
            }

            TempData["Success"] = "Eger e-posta kayitliysa sifre sifirlama baglantisi gonderildi.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string email, string token)
        {
            email = (email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["Warning"] = "Baglanti gecersiz veya suresi dolmus olabilir.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !IsResetTokenValid(user, token))
            {
                TempData["Warning"] = "Baglanti gecersiz veya suresi dolmus olabilir.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string password, string confirmPassword)
        {
            email = (email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                ModelState.AddModelError(string.Empty, "Baglanti gecersiz veya suresi dolmus olabilir.");
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(confirmPassword), "Sifreler ayni olmali.");
            }

            if (!ValidationRules.TryValidatePassword(password, out var passwordError))
            {
                ModelState.AddModelError(nameof(password), passwordError);
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !IsResetTokenValid(user, token))
            {
                ModelState.AddModelError(string.Empty, "Baglanti gecersiz veya suresi dolmus olabilir.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            user!.PasswordHash = PasswordHasher.Hash(password);
            user.PasswordResetTokenHash = null;
            user.PasswordResetExpiresAt = null;
            user.EmailConfirmed = true;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Sifreniz guncellendi. Giris yapabilirsiniz.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied() => View();

        private async Task SignIn(AppUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }

        private IActionResult? RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return null;
        }

        private static bool IsResetTokenValid(AppUser user, string token)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordResetTokenHash) || !user.PasswordResetExpiresAt.HasValue)
                return false;

            if (user.PasswordResetExpiresAt.Value < DateTime.UtcNow)
                return false;

            var hash = PasswordHasher.Hash(token);
            return string.Equals(user.PasswordResetTokenHash, hash, StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string email, string token)
        {
            email = (email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["Warning"] = "Baglanti gecersiz veya suresi dolmus olabilir.";
                return RedirectToAction(nameof(Login));
            }

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !IsConfirmTokenValid(user, token))
            {
                TempData["Warning"] = "Baglanti gecersiz veya suresi dolmus olabilir.";
                return RedirectToAction(nameof(Login));
            }

            user.EmailConfirmed = true;
            user.EmailConfirmTokenHash = null;
            user.EmailConfirmExpiresAt = null;
            await _db.SaveChangesAsync();

            TempData["Success"] = "E-posta onaylandi. Giris yapabilirsiniz.";
            return RedirectToAction(nameof(Login));
        }

        private static bool IsConfirmTokenValid(AppUser user, string token)
        {
            if (user.EmailConfirmed)
                return true;

            if (string.IsNullOrWhiteSpace(user.EmailConfirmTokenHash) || !user.EmailConfirmExpiresAt.HasValue)
                return false;

            if (user.EmailConfirmExpiresAt.Value < DateTime.UtcNow)
                return false;

            var hash = PasswordHasher.Hash(token);
            return string.Equals(user.EmailConfirmTokenHash, hash, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> SendConfirmationEmailAsync(AppUser user, string? rawToken = null)
        {
            var token = rawToken ?? TokenGenerator.CreateToken();
            user.EmailConfirmTokenHash = PasswordHasher.Hash(token);
            user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);
            await _db.SaveChangesAsync();

            var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email = user.Email, token }, Request.Scheme);
            if (!string.IsNullOrWhiteSpace(confirmLink))
            {
                var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                await _emailSender.SendAsync(user.Email, "E-posta Onayi", body);
            }

            return confirmLink;
        }
    }
}
