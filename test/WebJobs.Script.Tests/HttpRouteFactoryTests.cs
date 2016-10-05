﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpRouteFactoryTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("testfunction", "")]
        [InlineData("a/{b}/c/{d}", "b,d")]
        [InlineData("a/{b:alpha}/c/{d:int?}", "b,d")]
        public static void GetRouteParameters_ReturnsExpectedResult(string routeTemplate, string expected)
        {
            HttpRouteFactory routeFactory = new HttpRouteFactory("api");
            var parameters = routeFactory.GetRouteParameters(routeTemplate);
            var result = string.Join(",", parameters);
            Assert.Equal(expected, result);
        }

        [Fact]
        public static void AddRoute_AmbiguousRoute_FirstRouteIsChosen()
        {
            HttpRouteFactory routeFactory = new HttpRouteFactory("api");

            HttpRouteCollection routes = new HttpRouteCollection();
            var route1 = routeFactory.AddRoute("route1", "foo/bar/baz", routes);
            var route2 = routeFactory.AddRoute("route2", "foo/bar/baz", routes);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://host/api/foo/bar/baz");
            var routeData = routes.GetRouteData(request);
            Assert.Same(route1, routeData.Route);
        }
    }
}
