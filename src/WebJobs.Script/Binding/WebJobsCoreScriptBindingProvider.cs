// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Enables all Core SDK Triggers/Binders.
    /// </summary>
    internal class WebJobsCoreScriptBindingProvider : ScriptBindingProvider
    {
        private readonly RouteHandlingOptions _routeHandlingOptions;

        // Back-compat constructor used by unit tests and other callers that don't supply RouteHandlingOptions.
        public WebJobsCoreScriptBindingProvider(ILogger<WebJobsCoreScriptBindingProvider> logger)
            : this(logger, routeHandlingOptions: null)
        {
        }

        public WebJobsCoreScriptBindingProvider(ILogger<WebJobsCoreScriptBindingProvider> logger, IOptions<RouteHandlingOptions> routeHandlingOptions)
            : base(logger)
        {
            _routeHandlingOptions = routeHandlingOptions?.Value;
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;

            if (string.Compare(context.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new HttpScriptBinding(context, _routeHandlingOptions);
            }

            return binding != null;
        }

        private class HttpScriptBinding : ScriptBinding
        {
            private readonly RouteHandlingOptions _parentRouteHandlingOptions;

            public HttpScriptBinding(ScriptBindingContext context, RouteHandlingOptions routeHandlingOptions) : base(context)
            {
                _parentRouteHandlingOptions = routeHandlingOptions;
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(HttpRequest);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                AuthorizationLevel defaultLevel = AuthorizationLevel.Function;
                if (!string.IsNullOrEmpty(_parentRouteHandlingOptions?.AuthenticationLevel))
                {
                    try
                    {
                        defaultLevel = (AuthorizationLevel)Enum.Parse(typeof(AuthorizationLevel), _parentRouteHandlingOptions.AuthenticationLevel, ignoreCase: true);
                    }
                    catch
                    {
                        // ignore invalid configuration and fall back to Function
                    }
                }

                var authLevel = Context.GetMetadataEnumValue<AuthorizationLevel>("authLevel", defaultLevel);

                JArray methodArray = Context.GetMetadataValue<JArray>("methods");
                string[] methods = null;
                if (methodArray != null)
                {
                    methods = methodArray.Select(p => p.Value<string>()).ToArray();
                }

                var attribute = new HttpTriggerAttribute(authLevel, methods)
                {
                    Route = Context.GetMetadataValue<string>("route")
                };

                return new Collection<Attribute> { attribute };
            }
        }
    }
}
