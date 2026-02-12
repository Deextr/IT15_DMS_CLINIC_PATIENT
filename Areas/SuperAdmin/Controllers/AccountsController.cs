using System.Security.Claims;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Accounts;
using DMS_CPMS.Models.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class AccountsController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;

        private const int PageSize = 10;

        public AccountsController(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        private static string FullName(ApplicationUser u)
        {
            var full = $"{u.FirstName ?? ""} {u.LastName ?? ""}".Trim();
            return string.IsNullOrEmpty(full) ? (u.UserName ?? "") : full;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm, string? role, string? status, int page = 1)
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var combined = adminUsers
                .Select(u => (User: u, Role: "Admin"))
                .Concat(staffUsers.Select(u => (User: u, Role: "Staff")))
                .GroupBy(x => x.User.Id)
                .Select(g => g.First())
                .Select(x => new AccountListItemViewModel
                {
                    Id = x.User.Id,
                    Username = x.User.UserName ?? "",
                    FirstName = x.User.FirstName ?? "",
                    LastName = x.User.LastName ?? "",
                    Role = x.Role,
                    IsActive = x.User.IsActive
                })
                .AsQueryable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                combined = combined.Where(u => 
                    u.Username.Contains(searchTerm) || 
                    u.FirstName.Contains(searchTerm) ||
                    u.LastName.Contains(searchTerm));
            }

            // Apply role filter
            if (!string.IsNullOrWhiteSpace(role))
            {
                combined = combined.Where(u => u.Role == role);
            }

            // Apply status filter
            if (!string.IsNullOrWhiteSpace(status))
            {
                bool isActive = status == "Active";
                combined = combined.Where(u => u.IsActive == isActive);
            }

            var totalCount = combined.Count();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var accounts = combined
                .OrderBy(x => x.Username)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            var model = new AccountsIndexViewModel
            {
                Accounts = accounts,
                SearchTerm = searchTerm,
                RoleFilter = role,
                StatusFilter = status,
                PageNumber = page,
                TotalPages = totalPages,
                TotalCount = totalCount,
                NewAccount = new CreateAccountViewModel()
            };

            return View("~/Views/SuperAdmin/Accounts/Index.cshtml", model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var model = new CreateAccountViewModel();
            return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAccountViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
            }

            if (!string.Equals(model.RoleType, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(model.RoleType, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.RoleType), "You can only assign Admin or Staff roles.");
                return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
            }

            if (!await _roleManager.RoleExistsAsync(model.RoleType))
            {
                ModelState.AddModelError(nameof(model.RoleType), "Selected role does not exist.");
                return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
            }

            var existingUser = await _userManager.FindByNameAsync(model.Username);
            if (existingUser != null)
            {
                ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
                return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Username,
                FirstName = model.FirstName,
                LastName = model.LastName,
                IsActive = true,
                Email = $"{model.Username}@example.com",
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);

            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return View("~/Views/SuperAdmin/Accounts/Create.cshtml", model);
            }

            user.FirstName = model.FirstName ?? string.Empty;
            user.LastName = model.LastName ?? string.Empty;
            await _userManager.UpdateAsync(user);

            var claims = new List<Claim>
            {
                new Claim("FirstName", model.FirstName ?? string.Empty),
                new Claim("LastName", model.LastName ?? string.Empty)
            };
            foreach (var claim in claims)
                await _userManager.AddClaimAsync(user, claim);

            await _userManager.AddToRoleAsync(user, model.RoleType);

            TempData["StatusMessage"] = "Account created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction(nameof(Index));

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return RedirectToAction(nameof(Index));

            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentId, id, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault(r => r == "Admin" || r == "Staff");
            if (role == null)
                return RedirectToAction(nameof(Index));

            var model = new EditAccountViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName ?? "",
                LastName = user.LastName ?? "",
                Username = user.UserName ?? "",
                RoleType = role,
                IsActive = user.IsActive
            };
            return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditAccountViewModel model)
        {
            if (!string.Equals(model.RoleType, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(model.RoleType, "Staff", StringComparison.OrdinalIgnoreCase))
                model.RoleType = "Staff";

            if (ModelState.IsValid == false)
                return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
                return RedirectToAction(nameof(Index));

            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentId, model.Id, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var roles = await _userManager.GetRolesAsync(user);
            if (!roles.Any(r => r == "Admin" || r == "Staff"))
                return RedirectToAction(nameof(Index));

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                if (model.NewPassword != model.ConfirmPassword)
                {
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "Password and confirmation do not match.");
                    return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);
                }
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
                if (!resetResult.Succeeded)
                {
                    foreach (var e in resetResult.Errors)
                        ModelState.AddModelError(string.Empty, e.Description);
                    return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);
                }
            }

            var usernameChanged = !string.Equals(user.UserName, model.Username, StringComparison.OrdinalIgnoreCase);
            if (usernameChanged)
            {
                var existing = await _userManager.FindByNameAsync(model.Username);
                if (existing != null)
                {
                    ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
                    return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);
                }
                user.UserName = model.Username;
            }

            user.FirstName = model.FirstName ?? "";
            user.LastName = model.LastName ?? "";
            user.Email = $"{model.Username}@example.com";
            user.IsActive = string.Equals(currentId, model.Id, StringComparison.OrdinalIgnoreCase) ? true : model.IsActive;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                foreach (var e in updateResult.Errors)
                    ModelState.AddModelError(string.Empty, e.Description);
                return View("~/Views/SuperAdmin/Accounts/Edit.cshtml", model);
            }

            var currentRole = roles.FirstOrDefault(r => r == "Admin" || r == "Staff");
            if (currentRole != null && !string.Equals(currentRole, model.RoleType, StringComparison.OrdinalIgnoreCase))
            {
                await _userManager.RemoveFromRoleAsync(user, currentRole);
                await _userManager.AddToRoleAsync(user, model.RoleType);
            }

            TempData["StatusMessage"] = "Account updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Deactivate(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction(nameof(Index));

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return RedirectToAction(nameof(Index));

            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentId, id, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own account.";
                return RedirectToAction(nameof(Index));
            }

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault(r => r == "Admin" || r == "Staff");
            if (role == null)
                return RedirectToAction(nameof(Index));

            var vm = new DeactivateAccountViewModel
            {
                Id = user.Id,
                Username = user.UserName ?? "",
                FullName = FullName(user),
                Role = role
            };
            return View("~/Views/SuperAdmin/Accounts/Deactivate.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Deactivate")]
        public async Task<IActionResult> DeactivateConfirm(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction(nameof(Index));

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return RedirectToAction(nameof(Index));

            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentId, id, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own account.";
                return RedirectToAction(nameof(Index));
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (!roles.Any(r => r == "Admin" || r == "Staff"))
                return RedirectToAction(nameof(Index));

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            TempData["StatusMessage"] = "Account has been deactivated. The user will no longer be able to log in.";
            return RedirectToAction(nameof(Index));
        }
    }
}
