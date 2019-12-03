// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using DotNetTI.BreakingChangeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    public interface IBreakingChangeAnalysisService
    {
        IEnumerable<AssemblyReport> LogBreakingChangeReport(CancellationToken cancellationToken);
    }
}