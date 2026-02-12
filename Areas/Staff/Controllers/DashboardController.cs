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
            // Placeholder view for Staff dashboard
            return View("~/Views/Staff/Dashboard.cshtml");
        }
    }
}

