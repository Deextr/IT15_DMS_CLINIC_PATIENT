using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Profile;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService             _auditLog;
        private readonly IWebHostEnvironment          _env;

        private static readonly string[] AllowedExtensions =
            { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        private static readonly string[] AllowedMimeTypes =
            { "image/jpeg", "image/png", "image/gif", "image/webp" };
        private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            IAuditLogService             auditLog,
            IWebHostEnvironment          env)
        {
            _userManager = userManager;
            _auditLog    = auditLog;
            _env         = env;
        }

        // ── POST /Profile/UpdateInfo ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateInfo(ProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles      = await _userManager.GetRolesAsync(user);
            var role       = roles.FirstOrDefault() ?? "";
            var canFullName = role is "SuperAdmin" or "Admin";

            var changed = new List<string>();
            var errors  = new List<string>();

            // Username – all roles
            if (!string.IsNullOrWhiteSpace(model.Username) &&
                !string.Equals(model.Username, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                var r = await _userManager.SetUserNameAsync(user, model.Username);
                if (r.Succeeded) changed.Add("Username");
                else errors.AddRange(r.Errors.Select(e => e.Description));
            }

            // Full name – SuperAdmin & Admin only
            if (canFullName)
            {
                if (!string.IsNullOrWhiteSpace(model.FirstName) && model.FirstName != user.FirstName)
                {
                    user.FirstName = model.FirstName;
                    changed.Add("First Name");
                }
                if (!string.IsNullOrWhiteSpace(model.LastName) && model.LastName != user.LastName)
                {
                    user.LastName = model.LastName;
                    changed.Add("Last Name");
                }
            }

            if (changed.Contains("First Name") || changed.Contains("Last Name"))
            {
                var r = await _userManager.UpdateAsync(user);
                if (!r.Succeeded) errors.AddRange(r.Errors.Select(e => e.Description));
            }

            if (errors.Any())
            {
                TempData["ProfileError"] = string.Join(" ", errors);
            }
            else if (changed.Any())
            {
                await _auditLog.LogAsync($"Profile updated: {string.Join(", ", changed)}");
                TempData["ProfileSuccess"] = "Profile information saved.";
            }

            return RedirectBack();
        }

        // ── POST /Profile/UpdatePassword ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(ProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(model.CurrentPassword) ||
                string.IsNullOrWhiteSpace(model.NewPassword)     ||
                string.IsNullOrWhiteSpace(model.ConfirmNewPassword))
            {
                TempData["ProfileError"] = "All password fields are required.";
                return RedirectBack();
            }

            if (model.NewPassword != model.ConfirmNewPassword)
            {
                TempData["ProfileError"] = "New password and confirmation do not match.";
                return RedirectBack();
            }

            var result = await _userManager.ChangePasswordAsync(
                user, model.CurrentPassword, model.NewPassword);

            if (result.Succeeded)
            {
                await _auditLog.LogAsync("Password changed via Profile");
                TempData["ProfileSuccess"] = "Password updated successfully.";
            }
            else
            {
                TempData["ProfileError"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectBack();
        }

        // ── POST /Profile/UpdateProfile (unified: info + picture + optional password) ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(ProfileViewModel model, IFormFile? profilePictureFile)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Check server-side attribute validation for the username field
            if (ModelState.TryGetValue("Username", out var usernameState) && usernameState.Errors.Any())
            {
                TempData["ProfileError"] = string.Join(" ", usernameState.Errors.Select(e => e.ErrorMessage));
                return RedirectBack();
            }

            var roles       = await _userManager.GetRolesAsync(user);
            var role        = roles.FirstOrDefault() ?? "";
            var canFullName = role is "SuperAdmin" or "Admin";

            var changed = new List<string>();
            var errors  = new List<string>();

            // ── Profile picture ───────────────────────────────────────────────
            if (profilePictureFile is { Length: > 0 })
            {
                if (profilePictureFile.Length > MaxBytes)
                {
                    errors.Add("Image must be smaller than 5 MB.");
                }
                else
                {
                    var ext  = Path.GetExtension(profilePictureFile.FileName).ToLowerInvariant();
                    var mime = profilePictureFile.ContentType.ToLowerInvariant();

                    if (!AllowedExtensions.Contains(ext) || !AllowedMimeTypes.Contains(mime))
                    {
                        errors.Add("Only JPG, PNG, GIF, or WebP images are allowed.");
                    }
                    else
                    {
                        // Remove old file
                        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
                        {
                            var oldFull = Path.Combine(
                                _env.WebRootPath,
                                user.ProfilePictureUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            if (System.IO.File.Exists(oldFull))
                                System.IO.File.Delete(oldFull);
                        }

                        var dir = Path.Combine(_env.WebRootPath, "uploads", "profile-pictures");
                        Directory.CreateDirectory(dir);
                        var fileName = $"{user.Id}_{Guid.NewGuid():N}{ext}";
                        var fullPath = Path.Combine(dir, fileName);

                        await using (var fs = new FileStream(fullPath, FileMode.Create))
                            await profilePictureFile.CopyToAsync(fs);

                        user.ProfilePictureUrl = $"/uploads/profile-pictures/{fileName}";
                        changed.Add("Profile Picture");
                    }
                }
            }

            // ── Username ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(model.Username) &&
                !string.Equals(model.Username, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                var r = await _userManager.SetUserNameAsync(user, model.Username);
                if (r.Succeeded) changed.Add("Username");
                else errors.AddRange(r.Errors.Select(e => e.Description));
            }

            // ── Full name (SuperAdmin & Admin only) ───────────────────────────
            if (canFullName)
            {
                if (!string.IsNullOrWhiteSpace(model.FirstName) && model.FirstName != user.FirstName)
                {
                    user.FirstName = model.FirstName;
                    changed.Add("First Name");
                }
                if (!string.IsNullOrWhiteSpace(model.LastName) && model.LastName != user.LastName)
                {
                    user.LastName = model.LastName;
                    changed.Add("Last Name");
                }
            }

            if (changed.Any(c => c is "First Name" or "Last Name" or "Profile Picture"))
            {
                var r = await _userManager.UpdateAsync(user);
                if (!r.Succeeded) errors.AddRange(r.Errors.Select(e => e.Description));
            }

            // ── Password (only when CurrentPassword is provided) ──────────────
            if (!string.IsNullOrWhiteSpace(model.CurrentPassword))
            {
                if (string.IsNullOrWhiteSpace(model.NewPassword) ||
                    string.IsNullOrWhiteSpace(model.ConfirmNewPassword))
                {
                    errors.Add("Please fill in all password fields.");
                }
                else if (model.NewPassword != model.ConfirmNewPassword)
                {
                    errors.Add("New password and confirmation do not match.");
                }
                else
                {
                    var r = await _userManager.ChangePasswordAsync(
                        user, model.CurrentPassword, model.NewPassword);
                    if (r.Succeeded)
                        changed.Add("Password");
                    else
                        errors.AddRange(r.Errors.Select(e => e.Description));
                }
            }

            if (errors.Any())
                TempData["ProfileError"] = string.Join(" ", errors);
            else if (changed.Any())
            {
                await _auditLog.LogAsync($"Profile updated: {string.Join(", ", changed)}");
                TempData["ProfileSuccess"] = "Profile saved successfully.";
            }

            return RedirectBack();
        }

        // ── POST /Profile/UpdatePicture ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePicture(IFormFile profilePictureFile)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (profilePictureFile is null || profilePictureFile.Length == 0)
            {
                TempData["ProfileError"] = "Please select an image file.";
                return RedirectBack();
            }
            if (profilePictureFile.Length > MaxBytes)
            {
                TempData["ProfileError"] = "Image must be smaller than 5 MB.";
                return RedirectBack();
            }

            var ext = Path.GetExtension(profilePictureFile.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                TempData["ProfileError"] = "Only JPG, PNG, GIF, or WebP images are allowed.";
                return RedirectBack();
            }

            // Server-side MIME type check (Content-Type header)
            var mime = profilePictureFile.ContentType.ToLowerInvariant();
            if (!AllowedMimeTypes.Contains(mime))
            {
                TempData["ProfileError"] = "Only JPG, PNG, GIF, or WebP images are allowed.";
                return RedirectBack();
            }

            // Remove old file
            if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
            {
                var oldFull = Path.Combine(
                    _env.WebRootPath,
                    user.ProfilePictureUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldFull))
                    System.IO.File.Delete(oldFull);
            }

            // Save new file
            var dir = Path.Combine(_env.WebRootPath, "uploads", "profile-pictures");
            Directory.CreateDirectory(dir);
            var fileName = $"{user.Id}_{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(dir, fileName);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
                await profilePictureFile.CopyToAsync(fs);

            user.ProfilePictureUrl = $"/uploads/profile-pictures/{fileName}";
            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                await _auditLog.LogAsync("Profile picture updated");
                TempData["ProfileSuccess"] = "Profile picture updated.";
            }
            else
            {
                TempData["ProfileError"] = "Could not save profile picture.";
            }

            return RedirectBack();
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private IActionResult RedirectBack()
        {
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && Url.IsLocalUrl(referer))
                return Redirect(referer);

            if (User.IsInRole("SuperAdmin"))
                return RedirectToAction("Index", "Dashboard", new { area = "SuperAdmin" });
            if (User.IsInRole("Admin"))
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
            if (User.IsInRole("Staff"))
                return RedirectToAction("Index", "Dashboard", new { area = "Staff" });

            return RedirectToAction("Login", "Login");
        }
    }
}
