// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Binds connected workers to the active ScriptHost extension endpoint catalog.
/// This class will associate a worker with a specific ScriptHost extension endpoint catalog. When a worker disconnects,
/// it will be unbound from its associated catalog. When a script host extension endpoint catalog begins draining, its
/// workers will be rebound to the newest active catalog. If during binding no script host is available, then the worker
/// will be added to the pending workers list until a catalog becomes available.
/// </summary>
internal sealed class ExtensionRpcEndpointRegistry : IExtensionRpcEndpointRouter
{
    private readonly Lock _syncLock = new();
    private readonly List<ScriptHostExtensionRpcEndpointCatalog> _catalogs = [];
    private readonly Dictionary<string, ScriptHostExtensionRpcEndpointCatalog> _workerBindings =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> _pendingWorkers = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask<ExtensionRpcEndpoint?> RouteAsync(
        string workerId, string method, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            return ValueTask.FromResult(
                _workerBindings.TryGetValue(workerId, out ScriptHostExtensionRpcEndpointCatalog? catalog)
                    ? catalog.TryAcquire(method) : null);
        }
    }

    /// <summary>
    /// Binds a connected worker to the newest active endpoint catalog.
    /// </summary>
    /// <param name="workerId">The connected worker identifier.</param>
    public void BindWorker(string workerId)
    {
        lock (_syncLock)
        {
            if (_workerBindings.ContainsKey(workerId))
            {
                return;
            }

            ScriptHostExtensionRpcEndpointCatalog? catalog = _catalogs.LastOrDefault(p => !p.IsDraining);
            if (catalog is null)
            {
                _pendingWorkers.Add(workerId);
                return;
            }

            _workerBindings.Add(workerId, catalog);
            _pendingWorkers.Remove(workerId);
        }
    }

    /// <summary>
    /// Removes the endpoint-catalog binding for a disconnected worker.
    /// </summary>
    /// <param name="workerId">The disconnected worker identifier.</param>
    public void UnbindWorker(string workerId)
    {
        lock (_syncLock)
        {
            _workerBindings.Remove(workerId);
            _pendingWorkers.Remove(workerId);
        }
    }

    /// <summary>
    /// Registers a ScriptHost endpoint catalog and rebinds workers to it.
    /// </summary>
    /// <param name="catalog">The endpoint catalog to register.</param>
    public void Register(ScriptHostExtensionRpcEndpointCatalog catalog)
    {
        lock (_syncLock)
        {
            if (_catalogs.Contains(catalog) || catalog.IsDraining)
            {
                return;
            }

            _catalogs.Add(catalog);
            foreach (string workerId in _workerBindings.Keys.ToArray())
            {
                _workerBindings[workerId] = catalog;
            }

            foreach (string workerId in _pendingWorkers)
            {
                _workerBindings.Add(workerId, catalog);
            }

            _pendingWorkers.Clear();
        }
    }

    /// <summary>
    /// Stops routing new calls to a catalog and rebinds its workers.
    /// </summary>
    /// <param name="catalog">The catalog beginning shutdown.</param>
    public void BeginDrain(ScriptHostExtensionRpcEndpointCatalog catalog)
    {
        lock (_syncLock)
        {
            catalog.BeginDrain();
            RebindWorkersUnsynchronized(catalog);
        }
    }

    /// <summary>
    /// Removes a catalog and rebinds any workers that still reference it.
    /// </summary>
    /// <param name="catalog">The catalog to unregister.</param>
    public void Unregister(ScriptHostExtensionRpcEndpointCatalog catalog)
    {
        lock (_syncLock)
        {
            _catalogs.Remove(catalog);
            RebindWorkersUnsynchronized(catalog);
        }
    }

    private void RebindWorkersUnsynchronized(ScriptHostExtensionRpcEndpointCatalog catalog)
    {
        string[] workerIds = [.. _workerBindings
            .Where(pair => ReferenceEquals(pair.Value, catalog))
            .Select(pair => pair.Key)];
        ScriptHostExtensionRpcEndpointCatalog? replacement = _catalogs.LastOrDefault(candidate => !candidate.IsDraining);
        foreach (string workerId in workerIds)
        {
            if (replacement is not null)
            {
                _workerBindings[workerId] = replacement;
            }
            else
            {
                _workerBindings.Remove(workerId);
                _pendingWorkers.Add(workerId);
            }
        }
    }
}
