// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class RouteHandlingOptions
    {
        private string _authenticationLevel;

        /// <summary>
        /// Mode determining how routes are mapped. "function" (default) maps httpTrigger functions
        /// defined in function metadata. "all" creates a single catch-all route that handles all
        /// requests and proxies it to the custom handler.
        /// </summary>
        public string Mode { get; set; } = "function";

        /// <summary>
        /// Only applicable to mode = "all". Determines the authentication level for the catch-all route.
        /// Allowed values: "function", "anonymous".
        /// Defaults to "function" when Mode == "all". When Mode == "function" this defaults to an empty string.
        /// </summary>

        public string AuthenticationLevel
        {
            get => _authenticationLevel == null
                ? (string.Equals(Mode, "all", StringComparison.OrdinalIgnoreCase) ? "function" : string.Empty)
                : _authenticationLevel;
            set => _authenticationLevel = value;
        }
    }
}
