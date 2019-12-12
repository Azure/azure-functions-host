// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    public class ChangeAnalysisState
    {
        public ChangeAnalysisState(DateTimeOffset analysisTime, object state = null)
        {
            LastAnalysisTime = analysisTime;
            Handle = state;
        }

        public DateTimeOffset LastAnalysisTime { get; }

        public object Handle { get; }
    }
}
