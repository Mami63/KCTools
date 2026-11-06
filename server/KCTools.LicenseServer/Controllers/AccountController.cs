using System.Threading.Tasks;
using KCTools.LicenseServer.Data;
using KCTools.LicenseServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace KCTools.LicenseServer.Controllers
{
    [AllowAnonymous]
    public sealed class AccountController : Controller
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;

        public AccountController(
            SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Admin");
            }

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // lockoutOnFailure: 5 hatalı denemede hesap kilitlenir.
            var result = await _signInManager.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);

                if (user != null)
                {
                    user.LastLoginAt = System.DateTimeOffset.UtcNow;
                    await _userManager.UpdateAsync(user);
                }

                if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return LocalRedirect(model.ReturnUrl);
                }

                if (user != null && await _userManager.IsInRoleAsync(user, AppRoles.Admin))
                {
                    return RedirectToAction("Index", "Admin");
                }

                return RedirectToAction("Index", "Portal");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty,
                    "Hesap geçici olarak kilitlendi. 15 dakika sonra tekrar deneyin.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "E-posta veya parola hatalı.");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Denied()
        {
            return View();
        }
    }
}
