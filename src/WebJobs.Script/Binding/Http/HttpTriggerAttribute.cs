// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class HttpTriggerAttribute : Attribute
    {
        public HttpTriggerAttribute()
        {
        }

        public string RouteTemplate { get; set; }

        // Handle "Route" vs. "RouteTemplate" naming mismatch. 
        // We could get rid of these if the names matched. 
        private class HttpTriggerAttributeMetadata : AttributeMetadata
        {
            public string Route { get; set; }

            public override Attribute GetAttribute()
            {
                return new HttpTriggerAttribute
                {
                    RouteTemplate = this.Route
                };
            }
        }
    }
}
