// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Observes when the specialized application configuration becomes available to worker links.
/// </summary>
/// <remarks>
/// A linked worker initializes with the application paths from <see cref="ScriptApplicationHostOptions"/>. In
/// placeholder mode those paths belong to the placeholder site, so a link waits until specialization replaces them
/// with the application's paths. This type only observes the configuration. It does not perform specialization or
/// wait for it to complete.
/// </remarks>
public sealed class WorkerLinkConfigurationMonitor
{
    private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkConfigurationMonitor"/> class.
    /// </summary>
    /// <param name="options">The application host options, which change when the runtime specializes.</param>
    public WorkerLinkConfigurationMonitor(IOptionsMonitor<ScriptApplicationHostOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>
    /// Waits until the application host options no longer describe the placeholder site.
    /// </summary>
    /// <param name="cancellationToken">Ends the wait. The caller owns any timeout.</param>
    /// <returns>A task that completes when the specialized configuration is available.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is canceled before the specialized configuration is available.
    /// </exception>
    /// <example>
    /// <code>
    /// using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    /// timeout.CancelAfter(TimeSpan.FromSeconds(10));
    /// await monitor.WaitForSpecializedConfigurationAsync(timeout.Token);
    /// </code>
    /// </example>
    public async Task WaitForSpecializedConfigurationAsync(CancellationToken cancellationToken)
    {
        if (!IsStandbyConfiguration())
        {
            return;
        }

        TaskCompletionSource specialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable? registration = _options.OnChange(options =>
        {
            if (!options.IsStandbyConfiguration)
            {
                specialized.TrySetResult();
            }
        });

        // Specialization can complete between the first check and the change registration.
        if (!IsStandbyConfiguration())
        {
            return;
        }

        await specialized.Task.WaitAsync(cancellationToken);
    }

    private bool IsStandbyConfiguration() => _options.CurrentValue.IsStandbyConfiguration;
}
