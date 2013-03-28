using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Monitor.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            var table = UsageStatsController.GetTable();

            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (var x in table.Enumerate())
            {
                var name = x.AccountName;
                int num;
                counts.TryGetValue(name, out num);
                num += x.VMExtraSmallSize;
                counts[name] = num;
            }

            IndexModel m = new IndexModel { Counts = counts };
            return View(m);
        }
    }

    public class IndexModel
    {
        public Dictionary<string, int> Counts { get; set; }
    }
}
