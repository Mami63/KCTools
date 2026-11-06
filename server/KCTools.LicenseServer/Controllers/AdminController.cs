using System;
using System.Linq;
using System.Threading.Tasks;
using KCTools.LicenseServer.Data;
using KCTools.LicenseServer.Models;
using KCTools.LicenseServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KCTools.LicenseServer.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public sealed class AdminController : Controller
    {
        private readonly LicenseDbContext _db;
        private readonly LicenseRepository _repository;
        private readonly UserManager<AppUser> _users;

        public AdminController(
            LicenseDbContext db, LicenseRepository repository, UserManager<AppUser> users)
        {
            _db = db;
            _repository = repository;
            _users = users;
        }

        private string Actor => User.Identity?.Name ?? "admin";

        private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

        public async Task<IActionResult> Index()
        {
            ViewBag.UserCount = await _db.Users.CountAsync();
            ViewBag.LicenseCount = await _db.Licenses.CountAsync();
            ViewBag.ActiveDevices = await _db.Devices.CountAsync(item => item.IsActive);

            var licenses = await _db.Licenses.ToListAsync();
            var now = DateTimeOffset.UtcNow;

            ViewBag.Expiring = licenses
                .Where(item => !item.IsRevoked && item.EndsAt <= now.AddDays(30))
                .OrderBy(item => item.EndsAt)
                .Take(10)
                .ToList();

            ViewBag.Recent = await _db.AuditLogs
                .OrderByDescending(item => item.Id)
                .Take(12)
                .ToListAsync();

            return View();
        }

        public async Task<IActionResult> Users()
        {
            var users = await _db.Users.ToListAsync();

            ViewBag.Roles = users.ToDictionary(
                item => item.Id,
                item => string.Join(",", _users.GetRolesAsync(item).GetAwaiter().GetResult()));

            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(CreateUserModel model)
        {
            var email = (model.Email ?? string.Empty).Trim();

            if (await _users.FindByEmailAsync(email) != null)
            {
                TempData["Error"] = "Bu e-posta zaten kayıtlı.";
                return RedirectToAction(nameof(Users));
            }

            var user = new AppUser { UserName = email, Email = email, DisplayName = email };
            var result = await _users.CreateAsync(user, model.Password ?? string.Empty);

            if (!result.Succeeded)
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(item => item.Description));
                return RedirectToAction(nameof(Users));
            }

            var role = model.Role == AppRoles.Admin ? AppRoles.Admin : AppRoles.Customer;
            await _users.AddToRoleAsync(user, role);

            await _repository.LogAuditAsync(
                Actor, "admin.user.create", "User", user.Id.ToString(), role, Ip);

            TempData["Message"] = "Kullanıcı oluşturuldu: " + email;
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUser(Guid id)
        {
            var user = await _users.FindByIdAsync(id.ToString());

            if (user == null)
            {
                TempData["Error"] = "Kullanıcı bulunamadı.";
                return RedirectToAction(nameof(Users));
            }

            user.IsActive = !user.IsActive;
            await _users.UpdateAsync(user);
            await _users.UpdateSecurityStampAsync(user);

            await _repository.LogAuditAsync(
                Actor, user.IsActive ? "admin.user.enable" : "admin.user.disable",
                "User", user.Id.ToString(), null, Ip);

            TempData["Message"] = user.IsActive ? "Kullanıcı etkinleştirildi." : "Kullanıcı devre dışı bırakıldı.";
            return RedirectToAction(nameof(Users));
        }

        public async Task<IActionResult> Licenses()
        {
            var licenses = await _repository.GetLicensesAsync();
            return View(licenses);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLicense(CreateLicenseModel model)
        {
            Guid? userId = null;

            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                var user = await _users.FindByEmailAsync(model.Email.Trim());

                if (user == null)
                {
                    TempData["Error"] = "Kullanıcı bulunamadı: " + model.Email;
                    return RedirectToAction(nameof(Licenses));
                }

                userId = user.Id;
            }

            var (license, error) = await _repository.CreateLicenseAsync(
                model.Key, model.Plan, model.Credits, model.Days, userId, Actor, Ip);

            if (license == null)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Message"] = "Lisans oluşturuldu: " + license.Key;
            }

            return RedirectToAction(nameof(Licenses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Extend(ExtendLicenseModel model)
        {
            var (license, error) = await _repository.ExtendLicenseAsync(
                model.Key, model.Days, Actor, Ip);

            TempData[license == null ? "Error" : "Message"] =
                license == null ? error : "Uzatıldı: " + license.Key;

            return RedirectToAction(nameof(Licenses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Freeze(string key, string? reason)
        {
            var (license, error) = await _repository.FreezeLicenseAsync(key, reason, Actor, Ip);

            TempData[license == null ? "Error" : "Message"] =
                license == null ? error : "Donduruldu: " + license.Key;

            return RedirectToAction(nameof(Licenses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unfreeze(string key)
        {
            var (license, error) = await _repository.UnfreezeLicenseAsync(key, Actor, Ip);

            TempData[license == null ? "Error" : "Message"] =
                license == null ? error : "Aktifleştirildi: " + license.Key;

            return RedirectToAction(nameof(Licenses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Revoke(RevokeLicenseModel model)
        {
            var (ok, error) = await _repository.RevokeLicenseAsync(
                model.Key, model.Reason, Actor, Ip);

            TempData[ok ? "Message" : "Error"] = ok ? "Lisans iptal edildi." : error;
            return RedirectToAction(nameof(Licenses));
        }

        public async Task<IActionResult> Devices()
        {
            var devices = await _repository.GetDevicesAsync();
            return View(devices);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetDevice(Guid id)
        {
            var (ok, error) = await _repository.ResetDeviceAsync(id, Actor, Ip);

            TempData[ok ? "Message" : "Error"] =
                ok ? "Cihaz bağlantısı sıfırlandı." : error;

            return RedirectToAction(nameof(Devices));
        }

        public async Task<IActionResult> Duration()
        {
            var licenses = await _repository.GetLicensesAsync();
            return View(licenses);
        }

        public async Task<IActionResult> Audit()
        {
            ViewBag.Logs = await _db.AuditLogs
                .OrderByDescending(item => item.Id)
                .Take(200)
                .ToListAsync();

            ViewBag.History = await _repository.GetActivationHistoryAsync(null, 200);
            return View();
        }

        public IActionResult Settings()
        {
            ViewBag.Provider = (Environment.GetEnvironmentVariable("KCTOOLS_DB_PROVIDER") ?? "postgres");
            ViewBag.HasJwtSecret = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("KCTOOLS_JWT_SECRET"));
            ViewBag.HasAdminKey = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("KCTOOLS_ADMIN_KEY"));
            ViewBag.OfflineHours = Environment.GetEnvironmentVariable("KCTOOLS_OFFLINE_HOURS") ?? "72";
            ViewBag.AutoMigrate = Environment.GetEnvironmentVariable("KCTOOLS_DB_AUTO_MIGRATE") ?? "0";
            return View();
        }
    }
}
