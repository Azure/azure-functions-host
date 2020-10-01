// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Provides method for returning Process metrics.
    /// </summary>
    public interface IProcessMetricsProvider
    {
        TimeSpan TotalProcessorTime { get; }
    }
}
