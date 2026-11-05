// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Polly;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Registers root-owned Host state publication to AppServer.
/// </summary>
internal static class AppServerServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the AppServer client and the hosted publisher to the root service collection.
        /// </summary>
        /// <returns>The supplied service collection.</returns>
        /// <remarks>Requires the runtime state manager registered by <c>AddRpcClientWebHostServices</c>.</remarks>
        public IServiceCollection AddAppServerHostStatePublisher()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton(TimeProvider.System);
            services.AddHttpClient(AppServerHostStateClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
            services.AddPolicyRegistry((provider, registry) =>
            {
                TimeProvider clock = provider.GetRequiredService<TimeProvider>();
                registry.Add(AppServerHostStatePublisher.PublicationPolicyName,
                    Policy<AppServerHostStatePublisher.PublicationOutcome>
                        .HandleResult(outcome => outcome is AppServerHostStatePublisher.PublicationOutcome.Retry)
                        .RetryForeverAsync(async (_, attempt, context) =>
                        {
                            if (!context.TryGetValue(AppServerHostStatePublisher.ChangeTaskKey, out object? value) || value is not Task changed
                                || !context.TryGetValue(AppServerHostStatePublisher.CancellationTokenKey, out object? token)
                                || token is not CancellationToken cancellationToken)
                            {
                                throw new InvalidOperationException("The publication retry is missing its runtime change signal or cancellation token.");
                            }

                            TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, Math.Min(attempt - 1, 7)), 30_000));
                            try
                            {
                                await changed.WaitAsync(delay, clock, cancellationToken);
                            }
                            catch (TimeoutException exception) when (!changed.IsFaulted && !Utility.IsFatal(exception))
                            {
                                // The retry delay elapsed without a newer snapshot.
                            }
                        }));
            });
            services.AddSingleton<IAppServerHostStateClient, AppServerHostStateClient>();
            services.AddSingleton<AppServerHostStatePublisher>();
            services.AddSingleton<IAppServerHostStatePublisher>(provider => provider.GetRequiredService<AppServerHostStatePublisher>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AppServerHostStatePublisher>());

            return services;
        }
    }
}
