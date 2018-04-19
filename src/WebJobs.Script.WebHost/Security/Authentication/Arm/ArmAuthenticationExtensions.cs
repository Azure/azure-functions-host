// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ArmAuthenticationExtensions
    {
        public static AuthenticationBuilder AddArmToken(this AuthenticationBuilder builder)
            => builder.AddScheme<ArmAuthenticationOptions, ArmAuthenticationHandler>(ArmAuthenticationDefaults.AuthenticationScheme, _ => { });
    }
}
