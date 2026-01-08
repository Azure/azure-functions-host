// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public static class AppCapabilitiesOptionsExtensions
    {
        public static OptionsBuilder<AppCapabilitiesOptions> AddFunctionAppCapabilities(
            this IServiceCollection services)
        {
            return services.AddOptions<AppCapabilitiesOptions>()
                           .Configure(_ => { }) // start with empty
                           .PostConfigure(options =>
                           {
                               // Final sanity pass (optional): validate names, normalize metadata, etc.
                           })
                           .Validate(options => IsValid(options), "Invalid capabilities options.");
        }

        public static OptionsBuilder<AppCapabilitiesOptions> ConfigureCapability(
            this OptionsBuilder<AppCapabilitiesOptions> builder,
            string name,
            string source,
            string? version = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
            return builder.Configure(options =>
            {
                AppCapabilityHelpers.AddOrUpdateCapability(
                    options.Capabilities,
                    name,
                    source,
                    version,
                    metadata);
            });
        }

        private static bool IsValid(AppCapabilitiesOptions o)
        {
            // Example validations: names non-empty, metadata size caps, etc.
            return o.Capabilities.Keys.All(k => !string.IsNullOrWhiteSpace(k));
        }
    }
}
