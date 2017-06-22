// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Authentication
{
    public class AuthenticationLevelOptions : AuthenticationSchemeOptions
    {
        public string Challenge { get; set; } = AuthLevelAuthenticationDefaults.AuthenticationScheme;
    }
}
