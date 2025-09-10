// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public static class BundlesServiceCollectionExtensions
    {
        public static IServiceCollection AddBundlesHttpClient(this IServiceCollection services)
        {
            services.AddHttpClient(nameof(ExtensionBundleManager), client =>
            {
                var hostVersion = typeof(ExtensionBundleManager).Assembly.GetName().Version?.ToString() ?? "unknown";
                var os = RuntimeInformation.OSDescription.Replace('(', '[').Replace(')', ']'); // avoid nesting parentheses issues
                var arch = RuntimeInformation.ProcessArchitecture.ToString();
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"FunctionsExtensionBundle/{hostVersion} ({os}; {arch})");
            })
                .AddPolicyHandler((sp, request) =>
                {
                    var logger = sp.GetRequiredService<ILogger<ExtensionBundleManager>>();

                    var policyBuilder = HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(resp => resp.StatusCode == HttpStatusCode.TooManyRequests);

                    return policyBuilder.WaitAndRetryAsync(
                        retryCount: 4,
                        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                        onRetry: (outcome, delay, attempt, ctx) =>
                        {
                            var statusCode = outcome.Result?.StatusCode;
                            var statusCodeDisplay = statusCode.HasValue ? ((int)statusCode.Value).ToString() : "None";
                            string azureRef = outcome.Result?.GetAzureRef();
                            logger.LogWarning(
                                outcome.Exception,
                                "Extension bundle download failure. Status: {StatusCode}, Attempt: {Attempt}, Uri: {Uri}, AzureRef: {AzureRef}. Retrying after {DelayMs}ms.",
                                statusCodeDisplay,
                                attempt,
                                request?.RequestUri,
                                azureRef,
                                delay.TotalMilliseconds);
                        });
                });
            return services;
        }
    }
}
