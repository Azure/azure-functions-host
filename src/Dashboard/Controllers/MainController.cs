using System;
using System.Web.Mvc;

namespace Dashboard.Controllers
{
    public class MainController : Controller
    {
        [Route("")]
        public ActionResult Index()
        {
            if (!Request.Url.GetLeftPart(UriPartial.Path).EndsWith("/"))
            {
                return RedirectPermanent(Request.Url.GetLeftPart(UriPartial.Path) + "/");
            }
            return View();
        }
	}
}
