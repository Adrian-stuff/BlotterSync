using BlotterSync.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlotterSync.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly BlotterSyncContext _context;

        public CategoriesController(BlotterSyncContext context)
        {
            _context = context;
        }

        // GET: api/Categories (Public so create modal can load all categories)
        [AllowAnonymous]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Category>>> GetCategories()
        {
            return await _context.Categories
                .OrderBy(c => c.CategoryId)
                .ToListAsync();
        }
    }
}
