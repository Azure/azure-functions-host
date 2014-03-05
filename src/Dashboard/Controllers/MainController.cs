using System.Web.Mvc;

namespace Dashboard.Controllers
{
    public class MainController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
	}
}