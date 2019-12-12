// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies
{
    public static class PolicyNames
    {
        public const string AdminAuthLevel = "AuthLevelAdmin";
        public const string AdminAuthLevelOrInternal = "InternalAuthLevelAdmin";
        public const string SystemAuthLevel = "AuthLevelSystem";
        public const string FunctionAuthLevel = "AuthLevelFunction";
        public const string SystemKeyAuthLevel = "AuthLevelSystemKey";
    }
}
