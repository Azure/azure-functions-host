// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class PrimaryHostCoordinatorOptionsSetup : IConfigureOptions<PrimaryHostCoordinatorOptions>
    {
        public void Configure(PrimaryHostCoordinatorOptions options)
        {
            // Azure Functions always requires the host coordinator to be enabled
            options.Enabled = true;
        }
    }
}
