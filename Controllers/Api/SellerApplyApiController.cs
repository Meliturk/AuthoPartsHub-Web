using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using AutoPartsWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers.Api
{
    [ApiController]
    [Route("api/seller/apply")]
    public class SellerApplyApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _env;

        public SellerApplyApiController(ApplicationDbContext db, IEmailSender emailSender, IWebHostEnvironment env)
        {
            _db = db;
            _emailSender = emailSender;
            _env = env;
        }

        [HttpPost]
        public async Task<IActionResult> Apply([FromBody] SellerApplyRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
                return BadRequest(new { message = "Gecerli bir e-posta girin." });
            if (!ValidationRules.TryValidatePassword(req.Password, out var passwordError))
                return BadRequest(new { message = passwordError });

            var exists = await _db.AppUsers.AnyAsync(u => u.Email == email);
            if (exists)
                return Conflict(new { message = "Bu e-posta ile kayit bulunuyor." });

            var user = new AppUser
            {
                FullName = req.FullName.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(req.Password),
                Role = "SellerPending",
                EmailConfirmed = false
            };
            var confirmToken = TokenGenerator.CreateToken();
            user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
            user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);
            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email, token = confirmToken }, Request.Scheme);
            if (!string.IsNullOrWhiteSpace(confirmLink))
            {
                var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                await _emailSender.SendAsync(email, "E-posta Onayi", body);
            }

            _db.SellerApplications.Add(new SellerApplication
            {
                UserId = user.Id,
                CompanyName = req.CompanyName.Trim(),
                ContactName = req.FullName.Trim(),
                Phone = req.Phone.Trim(),
                Address = req.Address.Trim(),
                TaxNumber = req.TaxNumber,
                Note = req.Note,
                Status = "Pending"
            });
            await _db.SaveChangesAsync();

            if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
            {
                return Accepted(new
                {
                    message = "Basvurunuz alindi. E-posta onayi gerekli.",
                    confirmLink,
                    confirmToken
                });
            }

            return Accepted(new { message = "Basvurunuz alindi. E-posta onayi gerekli." });
        }
    }
}
