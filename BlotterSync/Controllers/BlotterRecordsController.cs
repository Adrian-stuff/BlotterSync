using AutoMapper;
using BlotterSync.DTOs;
using BlotterSync.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BlotterSync.DTOs.BlotterRecordDTOs;

namespace BlotterSync.Controllers
{
    [Authorize] // Added this to lock down all endpoints in this controller
    [Route("api/[controller]")]
    [ApiController]
    public class BlotterRecordsController : ControllerBase
    {
        private readonly BlotterSyncContext _context;
        private readonly IMapper _mapper;

        public BlotterRecordsController(BlotterSyncContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        // ADMIN FUNCTION: Advanced Search and Filtering
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BlotterRecordDTO>>> GetBlotterRecords([FromQuery] string? searchStatus, [FromQuery] string? searchLocation)
        {
            var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userIdString == null) return Unauthorized();

            int userId = int.Parse(userIdString);
            var query = _context.BlotterRecords.AsQueryable();

            // Enforce Role Privacy
            if (userRole != "Admin")
            {
                query = query.Where(b => b.DeskOfficerId == userId);
            }

            // Apply Advanced Filters
            if (!string.IsNullOrEmpty(searchStatus))
            {
                query = query.Where(b => b.Status.Contains(searchStatus));
            }
            if (!string.IsNullOrEmpty(searchLocation))
            {
                query = query.Where(b => b.Location.Contains(searchLocation));
            }

            var records = await query.OrderByDescending(b => b.IncidentDate).ToListAsync();
            return Ok(_mapper.Map<IEnumerable<BlotterRecordDTO>>(records));
        }

        // ADMIN FUNCTION: Automated Report Formatting
        [Authorize(Roles = "Admin")] // Only Admins can export official reports
        [HttpGet("Export")]
        public async Task<IActionResult> ExportBlotterRecordsToCSV()
        {
            var records = await _context.BlotterRecords
                .OrderByDescending(b => b.IncidentDate)
                .ToListAsync();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Tracking Number,Incident Date,Location,Status,Narrative");

            foreach (var record in records)
            {
                // Clean the narrative text so commas don't break the CSV format
                var cleanNarrative = record.Narrative.Replace(",", ";").Replace("\n", " ");
                builder.AppendLine($"{record.TrackingNumber},{record.IncidentDate:yyyy-MM-dd HH:mm},{record.Location},{record.Status},{cleanNarrative}");
            }

            var csvBytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(csvBytes, "text/csv", $"Malanday_BlotterReport_{DateTime.Now:yyyyMMdd}.csv");
        }

        // POST: api/BlotterRecords
        [HttpPost]
        public async Task<ActionResult<BlotterRecordDTO>> CreateBlotterRecord(CreateBlotterRecordDTO createDto)
        {
            var blotterRecord = _mapper.Map<BlotterRecord>(createDto);

            blotterRecord.TrackingNumber = $"BS-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";
            blotterRecord.ReportedDate = DateTime.Now;
            blotterRecord.Status = "Pending";

            _context.BlotterRecords.Add(blotterRecord);
            await _context.SaveChangesAsync();

            var returnDto = _mapper.Map<BlotterRecordDTO>(blotterRecord);

            return CreatedAtAction(nameof(GetBlotterRecords), new { id = blotterRecord.RecordId }, returnDto);
        }

        // PUT: api/BlotterRecords/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBlotterRecord(int id, UpdateBlotterRecordDTO updateDto)
        {
            var existingRecord = await _context.BlotterRecords.FindAsync(id);
            if (existingRecord == null)
            {
                return NotFound();
            }

            _mapper.Map(updateDto, existingRecord);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BlotterRecordExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/BlotterRecords/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBlotterRecord(int id)
        {
            var record = await _context.BlotterRecords.FindAsync(id);
            if (record == null)
            {
                return NotFound();
            }

            _context.BlotterRecords.Remove(record);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool BlotterRecordExists(int id)
        {
            return _context.BlotterRecords.Any(e => e.RecordId == id);
        }
    }
}