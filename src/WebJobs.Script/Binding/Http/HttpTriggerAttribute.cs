// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    [Binding]
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class HttpTriggerAttribute : Attribute
    {
        public HttpTriggerAttribute()
        {
            AuthLevel = AuthorizationLevel.Function;
        }

        public HttpTriggerAttribute(AuthorizationLevel authLevel, params string[] methods)
        {
            AuthLevel = authLevel;
            Methods = methods;
        }

        /// <summary>
        /// Gets or sets the route template for the function. Can include
        /// route parameters using WebApi supported syntax. If not specified,
        /// will default to the function name.
        /// See: https://www.asp.net/web-api/overview/web-api-routing-and-actions/attribute-routing-in-web-api-2#constraints
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// Gets the http methods that are supported for the function.
        /// </summary>
        public string[] Methods { get; private set; }

        /// <summary>
        /// Gets the authorization level for the function.
        /// </summary>
        public AuthorizationLevel AuthLevel { get; private set; }

        /// <summary>
        /// Gets or sets the WebHook type, if this function represents a WebHook.
        /// </summary>
        public string WebHookType { get; set; }
    }
}
