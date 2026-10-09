using AutoMapper;
using BlotterSync.DTOs;
using BlotterSync.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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
            var query = _context.BlotterRecords.AsQueryable();

            // Enforce Role Privacy
            if (!IsAdmin())
            {
                var userId = GetCurrentOfficerId();
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

        // KIOSK FUNCTION: Returns ONLY safe data for the Kiosk map and charts (No login required)
        [AllowAnonymous]
        [HttpGet("Public")]
        public async Task<ActionResult<IEnumerable<object>>> GetPublicBlotterData()
        {
            var publicRecords = await _context.BlotterRecords
                .Select(b => new {
                    b.IncidentDate,
                    b.Location,
                    b.Status
                }).ToListAsync();

            return Ok(publicRecords);
        }

        // GET: api/BlotterRecords/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<BlotterRecordDTO>> GetBlotterRecord(int id)
        {
            var record = await _context.BlotterRecords.FindAsync(id);
            if (record == null || !CanAccess(record))
            {
                return NotFound();
            }

            return Ok(_mapper.Map<BlotterRecordDTO>(record));
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
                builder.AppendLine(string.Join(",",
                    CsvField(record.TrackingNumber),
                    CsvField(record.IncidentDate.ToString("yyyy-MM-dd HH:mm")),
                    CsvField(record.Location),
                    CsvField(record.Status),
                    CsvField(record.Narrative)));
            }

            // Prefix a UTF-8 BOM so Excel detects the encoding (names may contain ñ, etc.)
            var csvBytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString()))
                .ToArray();
            return File(csvBytes, "text/csv", $"Malanday_BlotterReport_{DateTime.Now:yyyyMMdd}.csv");
        }

        // POST: api/BlotterRecords
        [HttpPost]
        public async Task<ActionResult<BlotterRecordDTO>> CreateBlotterRecord(CreateBlotterRecordDTO createDto)
        {
            var referenceError = await ValidateReferencesAsync(createDto);
            if (referenceError != null)
            {
                return BadRequest(referenceError);
            }

            var blotterRecord = _mapper.Map<BlotterRecord>(createDto);

            // The filing officer is always the logged-in user, never a client-supplied ID
            blotterRecord.DeskOfficerId = GetCurrentOfficerId();
            blotterRecord.TrackingNumber = $"BS-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";
            blotterRecord.ReportedDate = DateTime.Now;
            blotterRecord.Status = "Pending";

            _context.BlotterRecords.Add(blotterRecord);
            await _context.SaveChangesAsync();

            var returnDto = _mapper.Map<BlotterRecordDTO>(blotterRecord);

            return CreatedAtAction(nameof(GetBlotterRecord), new { id = blotterRecord.RecordId }, returnDto);
        }

        // PUT: api/BlotterRecords/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateBlotterRecord(int id, UpdateBlotterRecordDTO updateDto)
        {
            var existingRecord = await _context.BlotterRecords.FindAsync(id);
            if (existingRecord == null || !CanAccess(existingRecord))
            {
                return NotFound();
            }

            _mapper.Map(updateDto, existingRecord);

            if (existingRecord.Status == "Resolved" && existingRecord.ResolutionDate == null)
            {
                existingRecord.ResolutionDate = DateTime.Now;
            }

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
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteBlotterRecord(int id)
        {
            var record = await _context.BlotterRecords.FindAsync(id);
            if (record == null || !CanAccess(record))
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

        private bool IsAdmin() => User.IsInRole("Admin");

        private int GetCurrentOfficerId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Officers may only touch records they filed; Admins may touch all of them
        private bool CanAccess(BlotterRecord record) => IsAdmin() || record.DeskOfficerId == GetCurrentOfficerId();

        private async Task<string?> ValidateReferencesAsync(CreateBlotterRecordDTO dto)
        {
            if (!await _context.Categories.AnyAsync(c => c.CategoryId == dto.CategoryId))
                return "Unknown incident category.";
            if (dto.ComplainantId != null && !await _context.Residents.AnyAsync(r => r.ResidentId == dto.ComplainantId))
                return "Complainant not found.";
            if (dto.RespondentId != null && !await _context.Residents.AnyAsync(r => r.ResidentId == dto.RespondentId))
                return "Respondent not found.";
            return null;
        }

        // RFC 4180 quoting, plus a leading apostrophe on values that spreadsheets would run as formulas
        private static string CsvField(string? value)
        {
            value ??= string.Empty;
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            {
                value = "'" + value;
            }
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
