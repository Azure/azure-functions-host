// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ManualTriggerHostBuilderExtensions
    {
        public static IHostBuilder AddManualTrigger(this IHostBuilder builder)
        {
            return builder.AddExtension<ManualTriggerConfigProvider>();
        }
    }
}
