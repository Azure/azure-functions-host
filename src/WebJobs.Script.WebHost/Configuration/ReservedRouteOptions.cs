// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal sealed class ReservedRouteOptions
    {
        public bool DisableReservedRouteEnforcement { get; set; }

        public bool AdminWarmupRouteEnabled { get; set; }
    }
}
