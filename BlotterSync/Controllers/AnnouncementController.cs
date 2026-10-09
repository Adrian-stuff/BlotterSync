using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlotterSync.Models;
using System.Security.Claims;

namespace BlotterSync.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnnouncementsController : ControllerBase
    {
        private readonly BlotterSyncContext _context;

        public AnnouncementsController(BlotterSyncContext context)
        {
            _context = context;
        }

        // KIOSK FUNCTION: Get the latest active announcement (No login required)
        [AllowAnonymous]
        [HttpGet("Active")]
        public async Task<ActionResult<Announcement>> GetActiveAnnouncement()
        {
            var announcement = await _context.Announcements
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.DatePosted)
                .FirstOrDefaultAsync();

            if (announcement == null) return NotFound("No active announcements.");
            return Ok(announcement);
        }

        // ADMIN FUNCTION: Post a new announcement
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateAnnouncement([FromBody] string message)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Deactivate old announcements
            var oldAnnouncements = await _context.Announcements.Where(a => a.IsActive).ToListAsync();
            foreach (var old in oldAnnouncements) { old.IsActive = false; }

            // Create new announcement
            var newAnnouncement = new Announcement
            {
                Message = message,
                DatePosted = DateTime.Now,
                PostedByOfficerId = int.Parse(userIdString!),
                IsActive = true
            };

            _context.Announcements.Add(newAnnouncement);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Announcement broadcasted successfully!" });
        }
    }
}