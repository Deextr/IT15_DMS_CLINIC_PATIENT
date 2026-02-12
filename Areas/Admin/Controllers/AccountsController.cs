using System.Security.Claims;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Accounts;
using DMS_CPMS.Models.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
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
        public async Task<IActionResult> Index(string? searchTerm, string? status, int page = 1)
        {
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var query = staffUsers
                .Select(u => new AccountListItemViewModel
                {
                    Id = u.Id,
                    Username = u.UserName ?? "",
                    FirstName = u.FirstName ?? "",
                    LastName = u.LastName ?? "",
                    Role = "Staff",
                    IsActive = u.IsActive
                })
                .AsQueryable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(u => 
                    u.Username.Contains(searchTerm) || 
                    u.FirstName.Contains(searchTerm) ||
                    u.LastName.Contains(searchTerm));
            }

            // Apply status filter
            if (!string.IsNullOrWhiteSpace(status))
            {
                bool isActive = status == "Active";
                query = query.Where(u => u.IsActive == isActive);
            }

            var totalCount = query.Count();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var accounts = query
                .OrderBy(x => x.Username)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            var model = new AccountsIndexViewModel
            {
                Accounts = accounts,
                SearchTerm = searchTerm,
                StatusFilter = status,
                PageNumber = page,
                TotalPages = totalPages,
                TotalCount = totalCount,
                NewAccount = new CreateAccountViewModel { RoleType = "Staff" }
            };

            return View("~/Views/Admin/Accounts/Index.cshtml", model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var model = new CreateAccountViewModel { RoleType = "Staff" };
            return View("~/Views/Admin/Accounts/Create.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAccountViewModel model)
        {
            model.RoleType = "Staff";

            if (!ModelState.IsValid)
                return View("~/Views/Admin/Accounts/Create.cshtml", model);

            if (!await _roleManager.RoleExistsAsync("Staff"))
            {
                ModelState.AddModelError(string.Empty, "The Staff role does not exist.");
                return View("~/Views/Admin/Accounts/Create.cshtml", model);
            }

            var existingUser = await _userManager.FindByNameAsync(model.Username);
            if (existingUser != null)
            {
                ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
                return View("~/Views/Admin/Accounts/Create.cshtml", model);
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
                return View("~/Views/Admin/Accounts/Create.cshtml", model);
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

            await _userManager.AddToRoleAsync(user, "Staff");

            TempData["StatusMessage"] = "Staff account created successfully.";
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

            if (!await _userManager.IsInRoleAsync(user, "Staff"))
                return RedirectToAction(nameof(Index));

            var model = new EditAccountViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName ?? "",
                LastName = user.LastName ?? "",
                Username = user.UserName ?? "",
                RoleType = "Staff",
                IsActive = user.IsActive
            };
            return View("~/Views/Admin/Accounts/Edit.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditAccountViewModel model)
        {
            model.RoleType = "Staff";

            if (!ModelState.IsValid)
                return View("~/Views/Admin/Accounts/Edit.cshtml", model);

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
                return RedirectToAction(nameof(Index));

            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentId, model.Id, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            if (!await _userManager.IsInRoleAsync(user, "Staff"))
                return RedirectToAction(nameof(Index));

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                if (model.NewPassword != model.ConfirmPassword)
                {
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "Password and confirmation do not match.");
                    return View("~/Views/Admin/Accounts/Edit.cshtml", model);
                }
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
                if (!resetResult.Succeeded)
                {
                    foreach (var e in resetResult.Errors)
                        ModelState.AddModelError(string.Empty, e.Description);
                    return View("~/Views/Admin/Accounts/Edit.cshtml", model);
                }
            }

            var usernameChanged = !string.Equals(user.UserName, model.Username, StringComparison.OrdinalIgnoreCase);
            if (usernameChanged)
            {
                var existing = await _userManager.FindByNameAsync(model.Username);
                if (existing != null)
                {
                    ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
                    return View("~/Views/Admin/Accounts/Edit.cshtml", model);
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
                return View("~/Views/Admin/Accounts/Edit.cshtml", model);
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

            if (!await _userManager.IsInRoleAsync(user, "Staff"))
                return RedirectToAction(nameof(Index));

            var vm = new DeactivateAccountViewModel
            {
                Id = user.Id,
                Username = user.UserName ?? "",
                FullName = FullName(user),
                Role = "Staff"
            };
            return View("~/Views/Admin/Accounts/Deactivate.cshtml", vm);
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

            if (!await _userManager.IsInRoleAsync(user, "Staff"))
                return RedirectToAction(nameof(Index));

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            TempData["StatusMessage"] = "Account has been deactivated. The user will no longer be able to log in.";
            return RedirectToAction(nameof(Index));
        }
    }
}
