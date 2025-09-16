// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    /// <summary>
    /// Defines the routing mode for HTTP requests.
    /// </summary>
    public enum RouteHandlingMode
    {
        /// <summary>
        /// Routes are mapped based on httpTrigger functions defined in function metadata (default).
        /// </summary>
        Function,

        /// <summary>
        /// Creates a single catch-all route that handles all requests and proxies them to the custom handler.
        /// </summary>
        All
    }

    public class RouteHandlingOptions
    {
        private AuthorizationLevel? _authenticationLevel;

        /// <summary>
        /// Mode determining how routes are mapped.
        /// </summary>
        public RouteHandlingMode Mode { get; set; } = RouteHandlingMode.Function;

        /// <summary>
        /// Only applicable to mode = "all". Determines the authentication level for the catch-all route.
        /// Defaults to "function" when Mode == "all". When Mode == "function" this defaults to null.
        /// Supports all AuthorizationLevel values: Anonymous, Function, User, Admin, System.
        /// </summary>
        public AuthorizationLevel? AuthenticationLevel
        {
            get => _authenticationLevel ??
                   (Mode == RouteHandlingMode.All ? AuthorizationLevel.Function : null);
            set => _authenticationLevel = value;
        }

        /// <summary>
        /// Gets the AuthorizationLevel for binding purposes.
        /// </summary>
        /// <returns>The AuthorizationLevel to use for binding.</returns>
        public AuthorizationLevel GetAuthorizationLevelForBinding()
        {
            return AuthenticationLevel ?? AuthorizationLevel.Function;
        }

        /// <summary>
        /// Gets the authentication level as a string for compatibility.
        /// </summary>
        /// <returns>The authentication level as a string, or null if not set.</returns>
        public string GetAuthenticationLevelString()
        {
            var authLevel = AuthenticationLevel;
            return authLevel?.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Creates HTTP handler metadata for the catch-all route when mode is "all".
        /// </summary>
        /// <returns>A FunctionMetadata object representing the HTTP handler.</returns>
        public Description.FunctionMetadata CreateHttpHandlerMetadata()
        {
            if (Mode != RouteHandlingMode.All)
            {
                throw new InvalidOperationException("HTTP handler metadata can only be created when Mode is 'All'");
            }

            var handler = new Description.FunctionMetadata()
            {
                Name = "http-handler"
            };

            var inputRaw = new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = "httpTrigger",
                ["authLevel"] = GetAuthenticationLevelString() ?? "function",
                ["direction"] = "in",
                ["name"] = "req",
                ["methods"] = new Newtonsoft.Json.Linq.JArray("get", "post", "put", "delete", "patch", "head", "options"),
                ["route"] = "{*route}"
            };

            var outputRaw = new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = "http",
                ["direction"] = "out",
                ["name"] = "res"
            };

            handler.Bindings.Add(Description.BindingMetadata.Create(inputRaw));
            handler.Bindings.Add(Description.BindingMetadata.Create(outputRaw));

            return handler;
        }
    }

    /// <summary>
    /// Validator for RouteHandlingOptions configuration.
    /// </summary>
    public class RouteHandlingOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<RouteHandlingOptions>
    {
        public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string name, RouteHandlingOptions options)
        {
            if (options == null)
            {
                return Microsoft.Extensions.Options.ValidateOptionsResult.Success;
            }

            // AuthenticationLevel is only valid when mode = "all"
            if (options.Mode == RouteHandlingMode.Function && options.AuthenticationLevel.HasValue)
            {
                return Microsoft.Extensions.Options.ValidateOptionsResult.Fail(
                    "Invalid configuration: 'routeHandling.authenticationLevel' cannot be set when 'routeHandling.mode' is 'function'.");
            }

            return Microsoft.Extensions.Options.ValidateOptionsResult.Success;
        }
    }
}
