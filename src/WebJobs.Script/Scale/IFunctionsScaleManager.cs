// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public interface IFunctionsScaleManager
    {
        /// <summary>
        /// Returns scale monitors and target scalers we want to use based on the configuration.
        /// Scaler monitor will be ignored if a target scaler is defined in the same extensions assembly and TBS is enabled.
        /// </summary>
        /// <param name="scaleMonitorsToSample">Scale monitor to process.</param>
        /// <param name="targetScalersToSample">Target scaler to process.</param>
        public void GetScalersToSample(
            out List<IScaleMonitor> scaleMonitorsToSample,
            out List<ITargetScaler> targetScalersToSample);
    }
}
