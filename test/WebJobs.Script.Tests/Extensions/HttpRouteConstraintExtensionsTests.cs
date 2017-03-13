// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Web.Http.Routing;
using System.Web.Http.Routing.Constraints;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpRouteConstraintExtensionsTests
    {
        private Dictionary<IHttpRouteConstraint, SwaggerDataType> _swaggerDataTypeByhttpConstraint;

        public HttpRouteConstraintExtensionsTests()
        {
            _swaggerDataTypeByhttpConstraint = new Dictionary<IHttpRouteConstraint, SwaggerDataType>
            {
                { new AlphaRouteConstraint(), SwaggerDataType.String },
                { new MaxLengthRouteConstraint(10), SwaggerDataType.String },
                { new DateTimeRouteConstraint(), SwaggerDataType.String },
                { new RegexRouteConstraint(string.Empty), SwaggerDataType.String },
                { new GuidRouteConstraint(), SwaggerDataType.String },
                { new IntRouteConstraint(), SwaggerDataType.Integer },
                { new MaxRouteConstraint(10), SwaggerDataType.Integer },
                { new MinLengthRouteConstraint(10), SwaggerDataType.Integer },
                { new RangeRouteConstraint(0, 10), SwaggerDataType.Integer },
                { new LongRouteConstraint(), SwaggerDataType.Number },
                { new DecimalRouteConstraint(), SwaggerDataType.Number },
                { new DoubleRouteConstraint(), SwaggerDataType.Number },
                { new FloatRouteConstraint(), SwaggerDataType.Number },
                { new MinRouteConstraint(10), SwaggerDataType.Number },
                { new BoolRouteConstraint(), SwaggerDataType.Boolean }
            };
        }

        [Fact]
        public void ToSwagger_ReturnsCorrectCorrespondingSwaggerDataType()
        {
            // match the IHttpRouteConstraint with the corresponding swagger data type
            foreach (var httpRouteConstraint in _swaggerDataTypeByhttpConstraint.Keys)
            {
                Assert.Equal(httpRouteConstraint.ToSwaggerDataType(), _swaggerDataTypeByhttpConstraint[httpRouteConstraint]);
            }

            // unknown type should match against string
            var unknownHttpRouteConstraint = new OptionalRouteConstraint(new IntRouteConstraint());
            Assert.Equal(unknownHttpRouteConstraint.ToSwaggerDataType(), SwaggerDataType.String);
        }
    }
}
