using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public ActionResult GetLog()
        {
            IFrontEndSharedState state = new FrontEnd();
            string contents = state.GetLogContents();

            return new ContentResult { Content = contents, ContentType = "application/text", ContentEncoding = Encoding.UTF8 };
        }
    }


    public class LogModel
    {
        public string Content { get; set; }
    }

    public class HomeModel
    {
        public string ConfigInfo { get; set; }
        public ITriggerMap Map { get; set; }
    }
}
