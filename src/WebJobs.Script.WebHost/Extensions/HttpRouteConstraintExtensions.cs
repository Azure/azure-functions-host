// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Web.Http.Routing;
using System.Web.Http.Routing.Constraints;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class HttpRouteConstraintExtensions
    {
        private static Dictionary<Type, SwaggerDataType> _swaggerTypeByHttpConstraint = new Dictionary<Type, SwaggerDataType>()
        {
            { typeof(AlphaRouteConstraint), SwaggerDataType.String },
            { typeof(MaxLengthRouteConstraint), SwaggerDataType.String },
            { typeof(DateTimeRouteConstraint), SwaggerDataType.String },
            { typeof(RegexRouteConstraint), SwaggerDataType.String },
            { typeof(GuidRouteConstraint), SwaggerDataType.String },
            { typeof(IntRouteConstraint), SwaggerDataType.Integer },
            { typeof(MaxRouteConstraint), SwaggerDataType.Integer },
            { typeof(MinLengthRouteConstraint), SwaggerDataType.Integer },
            { typeof(RangeRouteConstraint), SwaggerDataType.Integer },
            { typeof(LongRouteConstraint), SwaggerDataType.Number },
            { typeof(DecimalRouteConstraint), SwaggerDataType.Number },
            { typeof(DoubleRouteConstraint), SwaggerDataType.Number },
            { typeof(FloatRouteConstraint), SwaggerDataType.Number },
            { typeof(MinRouteConstraint), SwaggerDataType.Number },
            { typeof(BoolRouteConstraint), SwaggerDataType.Boolean }
        };

        public static SwaggerDataType ToSwaggerDataType(this IHttpRouteConstraint httpRouteConstraint)
        {
            SwaggerDataType swaggerDataType;
            if (!_swaggerTypeByHttpConstraint.TryGetValue(httpRouteConstraint.GetType(), out swaggerDataType))
            {
                swaggerDataType = SwaggerDataType.String;
            }
            return swaggerDataType;
        }
    }
}