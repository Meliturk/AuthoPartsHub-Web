using AutoPartsWeb.Data;
using AutoPartsWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Controllers.Api
{
    [ApiController]
    [Route("api/vehicles")]
    public class VehiclesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public VehiclesApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<VehicleDto>>> Get()
        {
            var vehicles = await _db.Vehicles
                .OrderBy(v => v.Brand)
                .ThenBy(v => v.Model)
                .ThenBy(v => v.Year)
                .ToListAsync();

            var dto = vehicles
                .Select(v => new VehicleDto(v.Id, v.Brand, v.Model, v.Year, v.Engine, v.StartYear, v.EndYear, v.ImageUrl, v.BrandLogoUrl))
                .ToList();

            return Ok(dto);
        }
    }
}
