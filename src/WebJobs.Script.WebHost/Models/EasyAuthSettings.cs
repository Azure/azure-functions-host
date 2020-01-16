﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class EasyAuthSettings
    {
        public bool SiteAuthEnabled { get; set; }

        public string SiteAuthClientId { get; set; }

        public bool? SiteAuthAutoProvisioned { get; set; }
    }
}
