// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    public interface IChangeAnalysisStateProvider
    {
        Task<ChangeAnalysisState> GetCurrentAsync(CancellationToken cancellationToken);

        Task SetTimestampAsync(DateTimeOffset timestamp, object handle, CancellationToken cancellationToken);
    }
}
