// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
/*using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;*/
using Microsoft.Extensions.Logging;
/*using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;
using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;
using ParameterBindingType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.ParameterBinding.RpcDataOneofCase;*/

namespace Microsoft.Azure.WebJobs.Script
{
    public class WorkerFunctionMetadataManager : IWorkerFunctionMetadataManager
    {
        public WorkerFunctionMetadataManager() { }

        public void LogAndReturn(ILogger logger) { logger.LogInformation("Whatever text you want"); }

        public void ProcessFunctionMetadata(Collection<FunctionMetadata> metadataResponse)
        {
            throw new NotImplementedException();
        }

        public void WorkerGetFunctionMetadata()
        {
            throw new NotImplementedException();
        }
    }
}