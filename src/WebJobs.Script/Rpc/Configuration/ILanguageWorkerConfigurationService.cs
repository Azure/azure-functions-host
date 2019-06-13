// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface ILanguageWorkerConfigurationService
    {
        IList<WorkerConfig> WorkerConfigs { get; }

        void Reload(IConfiguration configuration);
    }
}
