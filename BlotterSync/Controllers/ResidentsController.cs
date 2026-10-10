using AutoMapper;
using BlotterSync.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BlotterSync.DTOs.ResidentDTOs;

namespace BlotterSync.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ResidentsController : ControllerBase
    {
        private readonly BlotterSyncContext _context;
        private readonly IMapper _mapper;

        public ResidentsController(BlotterSyncContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ResidentDTO>>> SearchResidents([FromQuery] string? searchName)
        {
            var query = _context.Residents.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchName))
            {
                query = query.Where(r => r.FirstName.Contains(searchName) || r.LastName.Contains(searchName));
            }

            var residents = await query.OrderBy(r => r.LastName).ToListAsync();
            return Ok(_mapper.Map<IEnumerable<ResidentDTO>>(residents));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ResidentDTO>> GetResident(int id)
        {
            var resident = await _context.Residents.FindAsync(id);

            if (resident == null)
            {
                return NotFound();
            }

            return Ok(_mapper.Map<ResidentDTO>(resident));
        }

        [HttpPost]
        public async Task<ActionResult<ResidentDTO>> CreateResident([FromBody] CreateResidentDTO createDto)
        {
            var resident = _mapper.Map<Resident>(createDto);

            _context.Residents.Add(resident);
            await _context.SaveChangesAsync();

            var returnDto = _mapper.Map<ResidentDTO>(resident);

            return CreatedAtAction(nameof(GetResident), new { id = resident.ResidentId }, returnDto);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateResident(int id, [FromBody] CreateResidentDTO updateDto)
        {
            var resident = await _context.Residents.FindAsync(id);

            if (resident == null)
            {
                return NotFound();
            }

            _mapper.Map(updateDto, resident);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ResidentExists(id))
                {
                    return NotFound();
                }

                throw;
            }

            return NoContent();
        }

        private bool ResidentExists(int id)
        {
            return _context.Residents.Any(e => e.ResidentId == id);
        }
    }
}