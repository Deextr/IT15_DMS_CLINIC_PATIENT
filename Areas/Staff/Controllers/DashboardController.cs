using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View("~/Views/Staff/Dashboard.cshtml");
        }

        public IActionResult Patients()
        {
            return View("~/Views/Staff/Patients.cshtml");
        }

        public IActionResult ArchiveRetention()
        {
            return View("~/Views/Staff/ArchiveRetention.cshtml");
        }
    }
}

