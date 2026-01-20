using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers
{
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _db;
        private const string CartSessionKey = "CartItems";

        public CartController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: /Cart
        public IActionResult Index()
        {
            var cart = GetCart();
            var total = cart.Sum(x => x.Price * x.Quantity);

            ViewBag.Total = total;
            ViewBag.CartCount = cart.Sum(x => x.Quantity);
            return View(cart);
        }

        [HttpGet]
        [Authorize(Roles = "User,SellerPending")]
        public IActionResult Checkout()
        {
            var cart = GetCart();
            var total = cart.Sum(x => x.Price * x.Quantity);
            if (cart.Count == 0)
            {
                TempData["Warning"] = "Sepetiniz boş.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Total = total;
            ViewBag.CartCount = cart.Sum(x => x.Quantity);
            return View(cart);
        }

        [HttpPost]
        [Authorize(Roles = "User,SellerPending")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(string customerName, string email, string address, string? city, string? phone)
        {
            var cart = GetCart();
            if (cart.Count == 0)
            {
                TempData["Warning"] = "Sepetiniz boş.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(address))
            {
                TempData["Warning"] = "Ad, e-posta ve adres zorunludur.";
                return RedirectToAction(nameof(Checkout));
            }

            // Kullanıcı bilgisiyle eşleştir ki 'Siparişlerim'de listelensin
            var identityEmail = User?.Identity?.Name;
            var appUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == identityEmail || u.FullName == identityEmail);
            if (appUser != null)
            {
                email = appUser.Email;
                if (string.IsNullOrWhiteSpace(customerName))
                    customerName = appUser.FullName ?? appUser.Email;
            }

            var total = cart.Sum(x => x.Price * x.Quantity);

            var order = new Order
            {
                CustomerName = customerName,
                Email = email,
                Address = address,
                City = city,
                Phone = phone,
                Status = "Pending",
                Total = total
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            var items = cart.Select(c => new OrderItem
            {
                OrderId = order.Id,
                PartId = c.PartId,
                Quantity = c.Quantity,
                UnitPrice = c.Price
            }).ToList();

            _db.OrderItems.AddRange(items);

            // Stok düşümü
            var partIds = items.Select(i => i.PartId).Distinct().ToList();
            var partsToUpdate = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync();
            foreach (var item in items)
            {
                var part = partsToUpdate.FirstOrDefault(p => p.Id == item.PartId);
                if (part != null)
                {
                    var newStock = part.Stock - item.Quantity;
                    part.Stock = newStock < 0 ? 0 : newStock;
                }
            }

            await _db.SaveChangesAsync();

            // Clear cart
            SaveCart(new List<CartItem>());
            TempData["Success"] = "Siparişiniz alındı.";
            return RedirectToAction(nameof(Index), "Parts");
        }

        // POST: /Cart/Add/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User,SellerPending")]
        public async Task<IActionResult> Add(int partId, int quantity = 1, string? returnUrl = null)
        {
            if (quantity <= 0) quantity = 1;

            var part = await _db.Parts
                .Include(p => p.Vehicle)
                .Include(p => p.PartVehicles).ThenInclude(pv => pv.Vehicle)
                .FirstOrDefaultAsync(p => p.Id == partId);

            if (part == null)
                return NotFound();

            var cart = GetCart();
            var existing = cart.FirstOrDefault(x => x.PartId == partId);

            if (existing == null)
            {
                var compat = part.PartVehicles?
                    .Select(pv => pv.Vehicle)
                    .Where(v => v != null)
                    .Select(v => $"{v!.Brand} {v.Model} ({v.Year})")
                    .Distinct()
                    .ToList();

                var vehicleText = compat != null && compat.Count > 0
                    ? string.Join(", ", compat)
                    : part.Vehicle == null ? null : $"{part.Vehicle.Brand} {part.Vehicle.Model} ({part.Vehicle.Year})";

                cart.Add(new CartItem
                {
                    PartId = part.Id,
                    Name = part.Name,
                    Brand = part.Brand,
                    Price = part.Price,
                    Quantity = quantity,
                    VehicleText = vehicleText
                });
            }
            else
            {
                existing.Quantity += quantity;
                if (existing.Quantity < 1) existing.Quantity = 1;
            }

            SaveCart(cart);
            TempData["Success"] = "Sepete eklendi.";

            return RedirectToLocal(returnUrl);
        }

        // POST: /Cart/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User,SellerPending")]
        public IActionResult Update(int partId, int quantity, string? returnUrl = null)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(x => x.PartId == partId);
            if (item != null)
            {
                item.Quantity = quantity < 1 ? 1 : quantity;
                SaveCart(cart);
                TempData["Success"] = "Sepet güncellendi.";
            }

            return RedirectToLocal(returnUrl);
        }

        // POST: /Cart/Remove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User,SellerPending")]
        public IActionResult Remove(int partId, string? returnUrl = null)
        {
            var cart = GetCart();
            cart.RemoveAll(x => x.PartId == partId);
            SaveCart(cart);
            TempData["Success"] = "Sepetten çıkarıldı.";

            return RedirectToLocal(returnUrl);
        }

        private List<CartItem> GetCart()
        {
            var json = HttpContext.Session.GetString(CartSessionKey);
            if (string.IsNullOrWhiteSpace(json))
                return new List<CartItem>();

            try
            {
                return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
            }
            catch
            {
                return new List<CartItem>();
            }
        }

        private void SaveCart(List<CartItem> cart)
        {
            var json = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(CartSessionKey, json);
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            if (Request.Headers.TryGetValue("Referer", out var referer))
            {
                var refererUrl = referer.ToString();
                if (!string.IsNullOrWhiteSpace(refererUrl) && Url.IsLocalUrl(refererUrl))
                    return Redirect(refererUrl);
            }

            return RedirectToAction("Index", "Parts");
        }
    }
}
