// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Authentication
{
    public class AuthenticationLevelOptions : AuthenticationSchemeOptions
    {
        public string Challenge { get; set; } = AuthLevelAuthenticationDefaults.AuthenticationScheme;
    }
}
