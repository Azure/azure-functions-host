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
            IFrontEndSharedState state = new FrontEnd();

            var map = state.GetMap();

            var model = new HomeModel
            {
                ConfigInfo = state.GetConfigInfo(),
                Map = map
            };

            return View(model);
        }
    }

    public class HomeModel
    {
        public string ConfigInfo { get; set; }
        public ITriggerMap Map { get; set; }
    }
}
