// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Holds worker links until the runtime uses the specialized application configuration.
/// </summary>
/// <remarks>
/// A linked worker initializes with the application paths from <see cref="ScriptApplicationHostOptions"/>. In
/// placeholder mode those paths belong to the placeholder site, so a link waits until specialization replaces them
/// with the application's paths.
/// </remarks>
public sealed class WorkerLinkSpecializationGate
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkSpecializationGate"/> class.
    /// </summary>
    /// <param name="options">The application host options, which change when the runtime specializes.</param>
    public WorkerLinkSpecializationGate(IOptionsMonitor<ScriptApplicationHostOptions> options)
        : this(options, DefaultTimeout)
    {
    }

    internal WorkerLinkSpecializationGate(IOptionsMonitor<ScriptApplicationHostOptions> options, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        _options = options;
        _timeout = timeout;
    }

    /// <summary>
    /// Waits until the application host options no longer describe the placeholder site.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>
    /// <see langword="true"/> when the options are specialized; <see langword="false"/> when they still describe the
    /// placeholder site after the timeout.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    /// <example>
    /// <code>
    /// if (!await gate.WaitForSpecializedConfigurationAsync(cancellationToken))
    /// {
    ///     return StatusCode(StatusCodes.Status503ServiceUnavailable);
    /// }
    /// </code>
    /// </example>
    public async Task<bool> WaitForSpecializedConfigurationAsync(CancellationToken cancellationToken)
    {
        if (!IsStandbyConfiguration())
        {
            return true;
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
            return true;
        }

        try
        {
            await specialized.Task.WaitAsync(_timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool IsStandbyConfiguration() => _options.CurrentValue.IsStandbyConfiguration;
}
