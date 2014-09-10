// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IHostIdProvider
    {
        // Ideally, we wouldn't pass the list of methods here and instead an implementation would get something like an
        // IFunctionIndex via DI if it needs it. Punting that for now.
        Task<string> GetHostIdAsync(IEnumerable<MethodInfo> indexedMethods, CancellationToken cancellationToken);
    }
}
