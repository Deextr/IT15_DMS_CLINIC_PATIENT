using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View("~/Views/Admin/Dashboard.cshtml");
        }

        public IActionResult ArchiveRetention()
        {
            return View("~/Views/Admin/ArchiveRetention.cshtml");
        }

        public IActionResult ActivityLogs()
        {
            return View("~/Views/Admin/ActivityLogs.cshtml");
        }
    }
}

