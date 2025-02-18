// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Used to track all startup operations.
    /// </summary>
    internal class ScriptHostStartupOperation : IDisposable
    {
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptHostStartupOperation"/> class.
        /// </summary>
        /// <param name="cancellationToken">A CancellationToken that will be linked and exposed via the <see cref="CancellationTokenSource"/> property.</param>
        /// <param name="parentId">The parent operation Id, if applicable.</param>
        public ScriptHostStartupOperation(CancellationToken cancellationToken, Guid? parentId = null)
        {
            Id = Guid.NewGuid();
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ParentId = parentId;
        }

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
        /// CancellationToken passed via the <see cref="ScriptHostStartupOperation"/> constructor.
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; }

        public void Dispose()
        {
            if (!_disposed)
            {
                CancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}