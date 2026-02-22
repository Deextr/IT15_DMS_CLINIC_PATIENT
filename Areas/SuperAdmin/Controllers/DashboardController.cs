using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View("~/Views/SuperAdmin/Dashboard.cshtml");
        }

        public IActionResult ArchiveRetention()
        {
            return RedirectToAction("Index", "ArchiveRetention", new { area = "" });
        }

        public IActionResult ActivityLogs()
        {
            return View("~/Views/SuperAdmin/ActivityLogs.cshtml");
        }
    }
}

