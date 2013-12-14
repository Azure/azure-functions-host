using System.Web;
using System.Web.Mvc;

namespace Microsoft.WindowsAzure.Jobs.Dashboard
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }
}