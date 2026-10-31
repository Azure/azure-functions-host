// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Coordinates publication of the authoritative Host snapshot to AppServer.
/// </summary>
internal sealed class HostStatePublicationCoordinator(
    IHostStateCoordinator hostStateCoordinator,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    private const int MaxStablePublishAttempts = 3;

    private readonly AppServerHostStateClient _appServerClient =
        new(httpClientFactory, configuration);

    private readonly IHostStateCoordinator _hostStateCoordinator =
        hostStateCoordinator ?? throw new ArgumentNullException(nameof(hostStateCoordinator));

    private readonly SemaphoreSlim _publicationLock = new(1, 1);

    public HostStateSnapshot Current => _hostStateCoordinator.Current;

    public Task<AppServerHostStatePublishResult> PublishAuthoritativeAsync(
        HostStateSnapshot state,
        CancellationToken cancellationToken)
        => PublishAsync(
            state,
            AppServerHostStatePublishMode.Authoritative,
            cancellationToken);

    public Task<AppServerHostStatePublishResult> PublishCapacityFenceAsync(
        HostStateSnapshot state,
        CancellationToken cancellationToken)
        => PublishAsync(
            state,
            AppServerHostStatePublishMode.CapacityFence,
            cancellationToken);

    public Task<(AppServerHostStatePublishResult Result, HostStateSnapshot State)>
        PublishCurrentAuthoritativeUntilStableAsync(CancellationToken cancellationToken)
        => PublishCurrentUntilStableAsync(
            AppServerHostStatePublishMode.Authoritative,
            cancellationToken);

    public Task<(AppServerHostStatePublishResult Result, HostStateSnapshot State)>
        PublishCurrentCapacityFenceUntilStableAsync(CancellationToken cancellationToken)
        => PublishCurrentUntilStableAsync(
            AppServerHostStatePublishMode.CapacityFence,
            cancellationToken);

    private async Task<AppServerHostStatePublishResult> PublishAsync(
        HostStateSnapshot state,
        AppServerHostStatePublishMode mode,
        CancellationToken cancellationToken)
    {
        await _publicationLock.WaitAsync(cancellationToken);
        try
        {
            return await _appServerClient.PublishAsync(state, mode, cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private async Task<(AppServerHostStatePublishResult Result, HostStateSnapshot State)>
        PublishCurrentUntilStableAsync(
            AppServerHostStatePublishMode mode,
            CancellationToken cancellationToken)
    {
        await _publicationLock.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 0; attempt < MaxStablePublishAttempts; attempt++)
            {
                HostStateSnapshot state = _hostStateCoordinator.Current;
                AppServerHostStatePublishResult result =
                    await _appServerClient.PublishAsync(state, mode, cancellationToken);
                if (result is not AppServerHostStatePublishResult.Applied ||
                    _hostStateCoordinator.Version == state.Version)
                {
                    return (result, state);
                }
            }

            return (AppServerHostStatePublishResult.TransientFailure, _hostStateCoordinator.Current);
        }
        finally
        {
            _publicationLock.Release();
        }
    }
}
