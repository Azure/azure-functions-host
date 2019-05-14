// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.HttpsPolicy;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class HostHstsOptions : HstsOptions
    {
        public bool IsEnabled { get; set; }
    }
}