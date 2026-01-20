using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Mvc;

namespace AutoPartsWeb.Controllers
{
    public class ContactController : Controller
    {
        private readonly ApplicationDbContext _db;

        public ContactController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(string name, string email, string? phone, string message)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(message))
            {
                TempData["Warning"] = "Ad, e-posta ve mesaj zorunlu.";
                return RedirectToAction("Contact", "Home");
            }

            var msg = new ContactMessage
            {
                Name = name.Trim(),
                Email = email.Trim(),
                Phone = phone?.Trim(),
                Message = message.Trim(),
                Status = "Yeni"
            };

            _db.ContactMessages.Add(msg);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Mesajınız alındı.";
            return RedirectToAction("Contact", "Home");
        }
    }
}
