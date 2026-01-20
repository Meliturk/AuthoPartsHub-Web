using System.Security.Claims;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using AutoPartsWeb.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly JwtTokenService _jwt;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _env;
        private const string AuthSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}";

        public AuthApiController(ApplicationDbContext db, JwtTokenService jwt, IEmailSender emailSender, IWebHostEnvironment env)
        {
            _db = db;
            _jwt = jwt;
            _emailSender = emailSender;
            _env = env;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] AuthRegisterRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
                return BadRequest(new { message = "Gecerli bir e-posta girin." });
            if (!ValidationRules.TryValidatePassword(req.Password, out var passwordError))
                return BadRequest(new { message = passwordError });

            var exists = await _db.AppUsers.AnyAsync(u => u.Email == email);
            if (exists)
                return Conflict(new { message = "Bu e-posta zaten kayitli." });

            var user = new AppUser
            {
                FullName = req.FullName.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(req.Password),
                Role = "User",
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

            if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
            {
                return Accepted(new
                {
                    message = "E-posta onayi gerekli. Lutfen e-postanizi kontrol edin.",
                    confirmLink,
                    confirmToken
                });
            }

            return Accepted(new { message = "E-posta onayi gerekli. Lutfen e-postanizi kontrol edin." });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] AuthLoginRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email.Trim());
            if (user == null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { message = "E-posta veya sifre hatali." });

            if (!user.EmailConfirmed)
            {
                var confirmToken = TokenGenerator.CreateToken();
                user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
                user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);
                await _db.SaveChangesAsync();

                var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email = user.Email, token = confirmToken }, Request.Scheme);
                if (!string.IsNullOrWhiteSpace(confirmLink))
                {
                    var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                    await _emailSender.SendAsync(user.Email, "E-posta Onayi", body);
                }

                if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
                {
                    return StatusCode(403, new
                    {
                        message = "E-posta dogrulanmamis. Onay linki yeniden gonderildi.",
                        confirmLink,
                        confirmToken
                    });
                }

                return StatusCode(403, new { message = "E-posta dogrulanmamis. Onay linki yeniden gonderildi." });
            }

            var token = _jwt.CreateToken(user);
            return Ok(new AuthResponseDto(user.Id, user.FullName, user.Email, user.Role, token));
        }

        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = AuthSchemes)]
        public async Task<ActionResult<AuthResponseDto>> Me()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Unauthorized();

            var token = _jwt.CreateToken(user);
            return Ok(new AuthResponseDto(user.Id, user.FullName, user.Email, user.Role, token));
        }

        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            var token = (req.Token ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email) || string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Gecersiz istek." });

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !IsConfirmTokenValid(user, token))
                return BadRequest(new { message = "Gecersiz veya suresi dolmus token." });

            user.EmailConfirmed = true;
            user.EmailConfirmTokenHash = null;
            user.EmailConfirmExpiresAt = null;
            await _db.SaveChangesAsync();

            return Ok(new { message = "E-posta onaylandi." });
        }

        [HttpPost("resend-confirm")]
        public async Task<IActionResult> ResendConfirm([FromBody] ResendConfirmRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
                return BadRequest(new { message = "Gecerli bir e-posta girin." });

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return Ok(new { message = "Eger e-posta kayitliysa onay linki gonderildi." });
            }

            if (user.EmailConfirmed)
            {
                return Ok(new { message = "E-posta zaten onayli." });
            }

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

            if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(confirmLink))
            {
                return Ok(new
                {
                    message = "Eger e-posta kayitliysa onay linki gonderildi.",
                    confirmLink,
                    confirmToken
                });
            }

            return Ok(new { message = "Eger e-posta kayitliysa onay linki gonderildi." });
        }

        [HttpPost("forgot")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
                return BadRequest(new { message = "Gecerli bir e-posta girin." });

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
                }

                if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(resetLink))
                {
                    return Ok(new
                    {
                        message = "Eger e-posta kayitliysa sifre sifirlama baglantisi gonderildi.",
                        resetLink,
                        resetToken = token
                    });
                }
            }

            return Ok(new { message = "Eger e-posta kayitliysa sifre sifirlama baglantisi gonderildi." });
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = (req.Email ?? "").Trim();
            var token = (req.Token ?? "").Trim();
            var password = req.Password ?? "";
            if (!ValidationRules.IsValidEmail(email) || string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Gecersiz istek." });

            if (!ValidationRules.TryValidatePassword(password, out var passwordError))
                return BadRequest(new { message = passwordError });

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !IsResetTokenValid(user, token))
                return BadRequest(new { message = "Gecersiz veya suresi dolmus token." });

            user.PasswordHash = PasswordHasher.Hash(password);
            user.PasswordResetTokenHash = null;
            user.PasswordResetExpiresAt = null;
            user.EmailConfirmed = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Sifreniz guncellendi." });
        }

        [HttpPut("update-profile")]
        [Authorize(AuthenticationSchemes = AuthSchemes)]
        public async Task<ActionResult<AuthResponseDto>> UpdateProfile([FromBody] UpdateProfileRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Unauthorized();

            var email = (req.Email ?? "").Trim();
            if (!ValidationRules.IsValidEmail(email))
                return BadRequest(new { message = "Gecerli bir e-posta girin." });

            // Email değiştirildiyse ve başka bir kullanıcıda varsa hata
            if (email != user.Email)
            {
                var emailExists = await _db.AppUsers.AnyAsync(u => u.Email == email && u.Id != userId);
                if (emailExists)
                    return Conflict(new { message = "Bu e-posta zaten kullaniliyor." });
                
                // Email değiştiyse e-posta onayını sıfırla
                user.EmailConfirmed = false;
                var confirmToken = TokenGenerator.CreateToken();
                user.EmailConfirmTokenHash = PasswordHasher.Hash(confirmToken);
                user.EmailConfirmExpiresAt = DateTime.UtcNow.AddHours(24);

                var confirmLink = Url.Action("ConfirmEmail", "Auth", new { email, token = confirmToken }, Request.Scheme);
                if (!string.IsNullOrWhiteSpace(confirmLink))
                {
                    var body = $"E-posta onay linkiniz: {confirmLink}\nBu baglanti 24 saat gecerlidir.";
                    await _emailSender.SendAsync(email, "E-posta Onayi", body);
                }
            }

            user.FullName = req.FullName.Trim();
            user.Email = email;
            await _db.SaveChangesAsync();

            var token = _jwt.CreateToken(user);
            return Ok(new AuthResponseDto(user.Id, user.FullName, user.Email, user.Role, token));
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
    }
}
