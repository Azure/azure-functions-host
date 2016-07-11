// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RoutingUtilityTests
    {
        [Fact]
        public void ExtractQueryArguments()
        {
            string template = "{strvalue}/{boolvalue:bool}/{longvalue:long}/{doubvalue:double}/{date:datetime}";
            var baseUri = new Uri("http://localhost/api/");
            var relativeUri = new Uri("mainpage/true/10000000000/3.14/2016-07-05", UriKind.Relative);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUri));
            var routeArguments = RoutingUtility.ExtractRouteParameters(template,
                request);

            var expectedDictionary = new RouteValueDictionary();
            expectedDictionary.Add("strvalue", "mainpage");
            expectedDictionary.Add("boolvalue", true);
            expectedDictionary.Add("longvalue", 10000000000);
            expectedDictionary.Add("doubvalue", 3.14);
            expectedDictionary.Add("date", DateTime.Parse("2016-07-05"));

            Assert.Equal(expectedDictionary, routeArguments);
        }

        [Fact]
        public void TestTypeConstraintMatches()
        {
            string template = "{strvalue}/{boolvalue:bool}/{longvalue:long}/{doubvalue:double}/{date:datetime}";
            //tests boolean constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/dafs/100000000000/3.14/2016-07-05"));
            //tests long constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/true/a/3.14/2016-07-05"));
            //tests double constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/true/100000000000/t/2016-07-05"));
            //tests date constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/true/100000000000/3.14/1"));
            //tests completely allowed route
            Assert.True(RoutingUtility.MatchesTemplate(template, "a/true/100000000000/3.14/2016-07-05"));
        }

        [Fact]
        public void TestMiscellaneousConstraintMatches()
        {
            string template = "{lenvalue:length(2,5)}/{alphavalue:alpha}/{limitedlong:range(0,100)}";
            //tests low end of string length constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/alpha/80"));
            //tests high end of string length constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "toolong/alpha/80"));
            //tests alphaconstraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/alpha1/80"));
            //tests low end of long range constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/alpha/-1"));
            //tests high end of long range constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/alpha/101"));
            //tests completely allowed route
            Assert.True(RoutingUtility.MatchesTemplate(template, "mid/alpha/100"));
        }

        [Fact]
        public void ExtractRouteFromMetdataWithNoRoute()
        {
            var mockMetadata = new Mock<FunctionMetadata>();
            var sampleName = RoutingUtility.ExtractRouteTemplateFromMetadata(mockMetadata.Object);
            Assert.Null(sampleName);
        }
    }
}
