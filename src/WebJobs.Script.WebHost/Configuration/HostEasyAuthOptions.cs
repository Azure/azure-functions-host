// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class HostEasyAuthOptions
    {
        public bool SiteAuthEnabled { get; set; }

        public string SiteAuthClientId { get; set; }

        public IConfiguration Configuration { get; set; }
    }
}
