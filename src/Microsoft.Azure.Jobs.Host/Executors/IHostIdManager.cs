// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    /// <summary>Defines a manager that maps a shared host name to an ID.</summary>
    /// <remarks>
    /// The host GUID serves as an key for lookup purposes, such as for host heartbeats and invocation queues.
    /// </remarks>
    internal interface IHostIdManager
    {
        Guid GetOrCreateHostId(string sharedHostName);
    }
}
