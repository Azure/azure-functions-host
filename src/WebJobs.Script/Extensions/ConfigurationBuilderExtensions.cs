// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddWebJobsExtensionOption(this IConfigurationBuilder builder)
        {
            return builder.Add(new WebJobsExtensionOptionSource());
        }
    }
}
