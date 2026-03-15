using System;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Models.AuditLog;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminDashboardService _dashboard;
        private const int PageSize = 8;

        public DashboardController(ApplicationDbContext context, IAdminDashboardService dashboard)
        {
            _context = context;
            _dashboard = dashboard;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _dashboard.GetDashboardAsync(null, null);
            return View("~/Views/SuperAdmin/Dashboard.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> DashboardData(string? range, string? startDate, string? endDate)
        {
            var data = await _dashboard.GetDashboardDataAsync(range, startDate, endDate);
            return Json(data);
        }

        public IActionResult ArchiveRetention()
        {
            return RedirectToAction("Index", "ArchiveRetention", new { area = "" });
        }

        [HttpGet]
        public async Task<IActionResult> AuditLogs(string? searchTerm, string? roleFilter, string? dateFrom, string? dateTo, int page = 1)
        {
            var query = _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Document)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(a =>
                    a.Action.ToLower().Contains(term) ||
                    (a.User != null && (a.User.FirstName + " " + a.User.LastName).ToLower().Contains(term)) ||
                    (a.Document != null && a.Document.DocumentTitle.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(roleFilter))
                query = query.Where(a => a.Role == roleFilter);

            if (DateTime.TryParse(dateFrom, out var fromDate))
                query = query.Where(a => a.Timestamp >= fromDate);

            if (DateTime.TryParse(dateTo, out var toDate))
                query = query.Where(a => a.Timestamp < toDate.AddDays(1));

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new AuditLogItemViewModel
                {
                    LogID = a.LogID,
                    DocumentID = a.DocumentID,
                    DocumentTitle = a.Document != null ? a.Document.DocumentTitle : null,
                    UserID = a.UserID,
                    UserFullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                    Role = a.Role ?? "Unknown",
                    Action = a.Action,
                    Timestamp = a.Timestamp
                })
                .ToListAsync();

            var model = new AuditLogIndexViewModel
            {
                Logs = logs,
                SearchTerm = searchTerm,
                RoleFilter = roleFilter,
                DateFrom = dateFrom,
                DateTo = dateTo,
                PageNumber = page,
                PageSize = PageSize,
                TotalPages = totalPages,
                TotalCount = totalCount
            };

            return View("~/Views/SuperAdmin/AuditLogs.cshtml", model);
        }
    }
}

