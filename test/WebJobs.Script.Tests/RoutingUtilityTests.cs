// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RoutingUtilityTests
    {
        [Fact]
        public void ExtractQueryArguments()
        {
            string template = "{strvalue}/{boolvalue:bool}/{date:datetime}/{decimalvalue:decimal}/{doubvalue:double}/{floatvalue:float}/{id:guid}/{intvalue:int}/{longvalue:long}";
            var baseUri = new Uri("http://localhost/api/");
            var relativeUri = new Uri("mainpage/true/2016-07-05/3.5/3.1496047/3.14/ca761232ed4211cebacd00aa0057b223/-3/10000000000", UriKind.Relative);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUri));
            var routeArguments = RoutingUtility.ExtractRouteParameters(template, request);

            var expectedDictionary = new RouteValueDictionary
            {
                { "strvalue", "mainpage" },
                { "boolvalue", true },
                { "date", DateTime.Parse("2016-07-05") },
                { "decimalvalue", 3.5m },
                { "doubvalue", 3.1496047 },
                { "floatvalue", (float)3.14 },
                { "id", new Guid("ca761232ed4211cebacd00aa0057b223") },
                { "intvalue", -3 },
                { "longvalue", 10000000000 }
            };

            Assert.Equal(expectedDictionary, routeArguments);
        }

        [Fact]
        public void TestConstraintMatches()
        {
            string template = RoutingUtility.EscapeRegexRoutes("{lenRange:length(2,5)}/{lenMax:maxlength(2)}/{lenMin:minlength(2)}/{alphaValue:alpha}/{longRange:range(0,100)}/{minLong:min(1)}/{maxLong:max(10)}/{*dateLike:regex(^\\d{4}/\\d{2}/\\d{2}$)}");
            //tests low end of string length constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "a/ab/ab/alpha/100/2/2/1994/13/06"));
            //tests high end of string length constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "toolong/ab/ab/alpha/100/2/2/1994/13/06"));
            //tests max string length constraing
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/abc/ab/alpha/100/2/2/1994/13/06"));
            //tests min string length constraing
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/a/alpha/100/2/2/1994/13/06"));
            //tests alphaconstraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha2/100/2/2/1994/13/06"));
            //tests low end of long range constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/-1/2/2/1994/13/06"));
            //tests high end of long range constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/101/2/2/1994/13/06"));
            //tests minimum constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/0/2/1994/13/06"));
            //tests maximum constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/11/1994/13/06"));
            //tests regex constraint
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/2/19947/13/06"));
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/2/1994/1/06"));
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/2/1994/13/086"));
            Assert.False(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/2/abcd/13/06"));
            //tests completely allowed route
            Assert.True(RoutingUtility.MatchesTemplate(template, "mid/ab/ab/alpha/100/2/2/1994/13/06"));
        }
    }
}
