// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class TestLanguageWorkerConfigurationService : ILanguageWorkerConfigurationService
    {
        private IList<WorkerConfig> _workerConfigs = TestHelpers.GetTestWorkerConfigs();

        public IList<WorkerConfig> WorkerConfigs => _workerConfigs;

        public void Reload(IConfiguration configuration)
        {
            throw new System.NotImplementedException();
        }
    }
}
