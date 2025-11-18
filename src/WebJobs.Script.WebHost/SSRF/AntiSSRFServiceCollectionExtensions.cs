// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Internal.AntiSSRF;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class AntiSSRFServiceCollectionExtensions
    {
        public static IServiceCollection AddAntiSSRFHttpClient(this IServiceCollection services)
        {
            // create and add SSRF HTTP client
            var policy = new AntiSSRFPolicy();
            policy.SetDefaults();
            var handler = policy.GetHandler();
            services.AddHttpClient(AntiSSRFConstants.AntiSSRFHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            return services;
        }
    }
}