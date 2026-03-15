using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Auth;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Controllers.Auth
{
    public class LoginController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService _auditLogService;

        public LoginController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IAuditLogService auditLogService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View("~/Views/Auth/Login.cshtml", new LoginViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View("~/Views/Auth/Login.cshtml", model);
            }

            var user = await _userManager.FindByNameAsync(model.Username);
            if (user != null && !user.IsActive)
            {
                var deactRoles = await _userManager.GetRolesAsync(user);
                await _auditLogService.LogAsync("Login Attempt (Deactivated)", user.Id);

                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact an administrator.");
                return View("~/Views/Auth/Login.cshtml", model);
            }

            var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, isPersistent: false, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var loggedInUser = await _userManager.FindByNameAsync(model.Username);
                if (loggedInUser is null)
                {
                    await _signInManager.SignOutAsync();
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return View("~/Views/Auth/Login.cshtml", model);
                }

                var roles = await _userManager.GetRolesAsync(loggedInUser);
                var role = roles.FirstOrDefault() ?? "Unknown";
                var fullName = $"{loggedInUser.FirstName} {loggedInUser.LastName}".Trim();

                await _auditLogService.LogAsync("Login", loggedInUser.Id);

                if (roles.Contains("SuperAdmin"))
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "SuperAdmin" });
                }

                if (roles.Contains("Admin"))
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
                }

                if (roles.Contains("Staff"))
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "Staff" });
                }

                // Fallback: use returnUrl or default dashboard
                return RedirectToLocal(returnUrl);
            }

            if (result.IsLockedOut)
            {
                if (user != null)
                    await _auditLogService.LogAsync("Account Locked Out", user.Id);

                ModelState.AddModelError(string.Empty, "Your account has been locked due to multiple failed login attempts. Please try again after 15 minutes.");
                return View("~/Views/Auth/Login.cshtml", model);
            }

            // Failed login attempt
            if (user != null)
            {
                var failRoles = await _userManager.GetRolesAsync(user);
                await _auditLogService.LogAsync("Failed Login", user.Id);
            }
            else
            {
                await _auditLogService.LogAsync("Failed Login");
            }

            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View("~/Views/Auth/Login.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _auditLogService.LogAsync("Logout");

            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Login));
        }
    }
}

