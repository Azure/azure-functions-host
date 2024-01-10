// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication
{
    public class SecurityConstants
    {
        public const string AuthLevelClaimType = "http://schemas.microsoft.com/2017/07/functions/claims/authlevel";
        public const string AuthLevelKeyNameClaimType = "http://schemas.microsoft.com/2017/07/functions/claims/keyid";

        /// <summary>
        /// Claim indicating whether a principal is authorized to invoke an http triggered function via a direct http
        /// request to the function. Note that this claim will not be required for invocations triggered via the
        /// /admin/functions/{function} API.
        /// </summary>
        public const string InvokeClaimType = "http://schemas.microsoft.com/2017/07/functions/claims/invoke";
    }
}
