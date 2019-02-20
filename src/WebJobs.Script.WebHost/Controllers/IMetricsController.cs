// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public interface IMetricsController
    {
        IMetricsLogger MetricsLogger
        {
            get;
        }

        string MetricsDescription
        {
            get;
        }
    }
}
