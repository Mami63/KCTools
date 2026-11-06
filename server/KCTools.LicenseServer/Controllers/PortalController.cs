using System.Linq;
using System.Threading.Tasks;
using KCTools.LicenseServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KCTools.LicenseServer.Controllers
{
    [Authorize]
    public sealed class PortalController : Controller
    {
        private readonly LicenseDbContext _db;
        private readonly UserManager<AppUser> _users;

        public PortalController(LicenseDbContext db, UserManager<AppUser> users)
        {
            _db = db;
            _users = users;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _users.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var licenses = await _db.Licenses
                .Include(item => item.Devices)
                .Where(item => item.UserId == user.Id)
                .ToListAsync();

            ViewBag.User = user;
            ViewBag.Roles = await _users.GetRolesAsync(user);

            return View(licenses);
        }
    }
}
