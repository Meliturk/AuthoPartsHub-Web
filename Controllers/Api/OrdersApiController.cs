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
    [Route("api/orders")]
    public class OrdersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private const string AuthSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}";

        public OrdersApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthSchemes, Roles = "User,SellerPending")]
        public async Task<ActionResult<OrderDto>> Create([FromBody] OrderCreateRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (req.Items == null || req.Items.Count == 0)
                return BadRequest(new { message = "Sepet boş." });

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (!string.IsNullOrWhiteSpace(userEmail))
                req = req with { Email = userEmail };

            var items = req.Items.Where(i => i.Quantity > 0).ToList();
            if (items.Count == 0)
                return BadRequest(new { message = "Geçerli ürün yok." });

            var partIds = items.Select(i => i.PartId).Distinct().ToList();
            var parts = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync();
            if (parts.Count != partIds.Count)
                return BadRequest(new { message = "Bazı ürünler bulunamadı." });

            var orderItems = new List<OrderItem>();
            foreach (var item in items)
            {
                var part = parts.First(p => p.Id == item.PartId);
                orderItems.Add(new OrderItem
                {
                    PartId = part.Id,
                    Quantity = item.Quantity,
                    UnitPrice = part.Price
                });

                var newStock = part.Stock - item.Quantity;
                part.Stock = newStock < 0 ? 0 : newStock;
            }

            var total = orderItems.Sum(i => i.UnitPrice * i.Quantity);
            var order = new Order
            {
                CustomerName = req.CustomerName.Trim(),
                Email = req.Email.Trim(),
                Address = req.Address.Trim(),
                City = req.City,
                Phone = req.Phone,
                Status = "Pending",
                Total = total,
                Items = orderItems
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            return Ok(MapOrder(order, parts));
        }

        [HttpGet("my")]
        [Authorize(AuthenticationSchemes = AuthSchemes, Roles = "User,SellerPending")]
        public async Task<ActionResult<IEnumerable<OrderDto>>> MyOrders()
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(userEmail)) return Unauthorized();

            var orders = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .Where(o => o.Email == userEmail)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var dto = orders.Select(o =>
            {
                var parts = o.Items.Select(i => i.Part).Where(p => p != null).ToList();
                return MapOrder(o, parts!);
            }).ToList();

            return Ok(dto);
        }

        [HttpGet("{id}")]
        [Authorize(AuthenticationSchemes = AuthSchemes, Roles = "User,SellerPending")]
        public async Task<ActionResult<OrderDto>> Get(int id)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(userEmail)) return Unauthorized();

            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            if (!string.Equals(order.Email, userEmail, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            var parts = order.Items.Select(i => i.Part).Where(p => p != null).ToList();
            return Ok(MapOrder(order, parts!));
        }

        private static OrderDto MapOrder(Order order, IEnumerable<Part> parts)
        {
            var itemDtos = order.Items.Select(i =>
            {
                var part = parts.FirstOrDefault(p => p.Id == i.PartId);
                return new OrderItemDto(i.PartId, part?.Name ?? $"Parça #{i.PartId}", i.Quantity, i.UnitPrice, part?.ImageUrl);
            }).ToList();

            return new OrderDto(
                order.Id,
                order.CreatedAt,
                order.Status,
                order.Total,
                order.CustomerName,
                order.Email,
                order.Address,
                order.City,
                order.Phone,
                itemDtos);
        }
    }
}
