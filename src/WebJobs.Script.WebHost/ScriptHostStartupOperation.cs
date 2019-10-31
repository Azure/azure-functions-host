// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Used to track all outstanding startup operations.
    /// </summary>
    internal class ScriptHostStartupOperation : IDisposable
    {
        // Use a dictionary here as we need the concurrency support, but also need to be able to remove
        // specific items from the collection.
        private static readonly ConcurrentDictionary<ScriptHostStartupOperation, object> _startupOperations = new ConcurrentDictionary<ScriptHostStartupOperation, object>();
        private readonly ILogger _logger;

        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptHostStartupOperation"/> class.
        /// </summary>
        /// <param name="cancellationToken">A CancellationToken that will be linked and exposed via the <see cref="CancellationTokenSource"/> property.</param>
        /// <param name="parentId">The parent operation Id, if applicable.</param>
        private ScriptHostStartupOperation(CancellationToken cancellationToken, ILogger logger, Guid? parentId = null)
        {
            Id = Guid.NewGuid();
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ParentId = parentId;
            _logger = logger;
        }

        /// <summary>
        /// Gets the list of currently active operations.
        /// </summary>
        public static ICollection<ScriptHostStartupOperation> ActiveOperations => _startupOperations.Keys;

        /// <summary>
        /// Gets the Id of the operation, used for tracking through logs.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the parent operation that started this one. Used when restarting during exceptions.
        /// </summary>
        public Guid? ParentId { get; }

        /// <summary>
        /// Gets the CancellationTokenSource used to cancel this operation. This is CancellationTokenSource is linked to the
        /// CancellationToken passed via the <see cref="Create(CancellationToken, ILogger, Guid?)"/> method.
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; }

        /// <summary>
        /// Creates a new <see cref="ScriptHostStartupOperation"/> and adds it to the <see cref="ActiveOperations"/> collection.
        /// </summary>
        /// <param name="cancellationToken">A CancellationToken to be linked to <see cref="CancellationTokenSource"/>.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="parentId">Ther parent operation id, if needed.</param>
        /// <returns>A new instance.</returns>
        public static ScriptHostStartupOperation Create(CancellationToken cancellationToken, ILogger logger, Guid? parentId = null)
        {
            var operation = new ScriptHostStartupOperation(cancellationToken, logger, parentId);
            _startupOperations.AddOrUpdate(operation, addValue: null, (k, v) => null);
            Log.StartupOperationCreated(logger, operation.Id, operation.ParentId);
            return operation;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _startupOperations.TryRemove(this, out object _);
                CancellationTokenSource?.Dispose();
                Log.StartupOperationCompleted(_logger, Id);
                _disposed = true;
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Guid, Guid?, Exception> _startupOperationCreated =
                LoggerMessage.Define<Guid, Guid?>(
                    LogLevel.Debug,
                    new EventId(524, nameof(StartupOperationCreated)),
                    "Startup operation '{operationId}' with parent id '{parentOperationId}' created.");

            private static readonly Action<ILogger, Guid, Exception> _startupOperationCompleted =
                LoggerMessage.Define<Guid>(
                    LogLevel.Debug,
                    new EventId(523, nameof(StartupOperationCompleted)),
                    "Startup operation '{operationId}' completed.");

            public static void StartupOperationCreated(ILogger logger, Guid operationId, Guid? parentOperationId)
            {
                _startupOperationCreated(logger, operationId, parentOperationId, null);
            }

            public static void StartupOperationCompleted(ILogger logger, Guid operationId)
            {
                _startupOperationCompleted(logger, operationId, null);
            }
        }
    }
}
