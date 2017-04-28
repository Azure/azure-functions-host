// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public sealed class AppServiceWorkerInfoProvider : IWorkerInfoProvider
    {
        private readonly IWorkerStatusProvider _provider;

        public AppServiceWorkerInfoProvider(IWorkerStatusProvider provider)
        {
            _provider = provider;
        }

        public async Task<IWorkerInfo> GetWorkerInfo(string activityId)
        {
            var loadFactor = await _provider.GetWorkerStatus(activityId);

            return new AppServiceWorkerInfo
            {
                PartitionKey = AppServiceSettings.WorkerPartitionKey,
                RowKey = AppServiceSettings.GetWorkerRowKey(AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName),
                StampName = AppServiceSettings.CurrentStampName,
                WorkerName = AppServiceSettings.WorkerName,
                LoadFactor = loadFactor
            };
        }
    }
}