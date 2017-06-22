// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization
{
    public class AuthLevelRequirement : IAuthorizationRequirement
    {
        public AuthLevelRequirement(AuthorizationLevel level)
        {
            Level = level;
        }

        public AuthorizationLevel Level { get; }
    }
}
