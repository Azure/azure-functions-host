// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IWorkerFunctionMetadataManager
    {
        void LogAndReturn(ILogger logger);

        void WorkerGetFunctionMetadata();

        void ProcessFunctionMetadata(Collection<FunctionMetadata> metadataResponse);
    }
}