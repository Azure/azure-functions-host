// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// A lightweight <see cref="IFunctionInvocationDispatcherFactory"/> that returns a
/// pre-built <see cref="ConnectedWorkerInvocationDispatcher"/> for external worker mode.
/// Replaces the default <see cref="FunctionInvocationDispatcherFactory"/> when
/// <c>FUNCTIONS_WORKER_EXTERNAL_ENABLED</c> is set to <c>true</c>.
/// </summary>
internal sealed class ExternalFunctionInvocationDispatcherFactory : IFunctionInvocationDispatcherFactory
{
    private readonly ConnectedWorkerInvocationDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalFunctionInvocationDispatcherFactory"/> class.
    /// </summary>
    /// <param name="dispatcher">The connected worker invocation dispatcher.</param>
    public ExternalFunctionInvocationDispatcherFactory(ConnectedWorkerInvocationDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new System.ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc/>
    public IFunctionInvocationDispatcher GetFunctionDispatcher() => _dispatcher;
}
