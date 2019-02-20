// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class MertricsActionAttributeTest
    {
        [Fact]
        public void MetricEvent_Added()
        {
            //Arrange
            IMetricsLogger metrics = new TestMetricsLogger();
            var controller = new Mock<IMetricsController>();
            controller.SetupGet(c => c.MetricsLogger).Returns(metrics);
            controller.SetupGet(c => c.MetricsDescription).Returns("test3");

            var httpContext = new DefaultHttpContext();
            var contextExecuting = new ActionExecutingContext(
                new ActionContext
                {
                    HttpContext = httpContext,
                    RouteData = new RouteData(new RouteValueDictionary { { "Controller", "test1" }, { "Action", "test2" } }),
                    ActionDescriptor = new ActionDescriptor(),
                },
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                controller.Object);

            var contextExecuted = new ActionExecutedContext(
                new ActionContext
                {
                    HttpContext = httpContext,
                    RouteData = new RouteData(new RouteValueDictionary { { "Controller", "test1" }, { "Action", "test2" } }),
                    ActionDescriptor = new ActionDescriptor(),
                },
                new List<IFilterMetadata>(),
                controller.Object);

            var att = new MertricsActionAttribute();

            //Act
            att.OnActionExecuting(contextExecuting);

            att.OnActionExecuted(contextExecuted);

            Assert.Equal(((TestMetricsLogger)metrics).EventsBegan.ToArray()[0].ToString(), "test3\\test1\\test2");
        }
    }
}
