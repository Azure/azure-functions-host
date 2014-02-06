using System;
using System.Collections.Generic;
using System.Web.Routing;

namespace Dashboard.ViewModels
{
    public class RunFunctionViewModel
    {
        public string FunctionName { get; set; }
        public IEnumerable<FunctionParameterViewModel> Parameters { get; set; }
        public Guid HostId { get; set; }
        public bool HostIsNotRunning { get; set; }
        public string FunctionId { get; set; }
        public RouteValueDictionary ActionRouteValues { get; set; }
    }
}
