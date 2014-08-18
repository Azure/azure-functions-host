// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>Defines a manager that maps a shared host name to an ID.</summary>
    /// <remarks>
    /// The host GUID serves as an key for lookup purposes, such as for host heartbeats and invocation queues.
    /// </remarks>
    internal interface IHostIdManager
    {
        Task<Guid> GetOrCreateHostIdAsync(string sharedHostName, CancellationToken cancellationToken);
    }
}
