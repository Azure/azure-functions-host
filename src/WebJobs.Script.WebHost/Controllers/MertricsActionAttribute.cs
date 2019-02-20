// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class MertricsActionAttribute : ActionFilterAttribute
    {
        private object _metricEvent;
        private IMetricsController _controller;

        public MertricsActionAttribute()
        {
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var controllerName = filterContext.RouteData.Values["Controller"];
            var actionName = filterContext.RouteData.Values["Action"];

            _controller = filterContext.Controller as IMetricsController;
            if (_controller != null)
            {
                _metricEvent = _controller.MetricsLogger.BeginEvent($"{_controller.MetricsDescription}\\{controllerName}\\{actionName}");
            }

            base.OnActionExecuting(filterContext);
        }

        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            if (_controller != null && _metricEvent != null)
            {
                _controller.MetricsLogger.EndEvent(_metricEvent);
            }
            base.OnActionExecuted(filterContext);
        }
    }
}
