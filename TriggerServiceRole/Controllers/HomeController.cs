using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using TriggerService;

namespace TriggerServiceRole.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            IFrontEndSharedState state = SharedState.GetState();

            var model = new HomeModel
            {
                Log = state.GetLog()
            };

            return View(model);
        }
    }

    public class HomeModel
    {
        public string Log { get; set; }
    }
}
