﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class TestFunctionRpcService
    {
        private IScriptEventManager _eventManager;
        private ILogger _logger;
        private string _workerId;
        private IDictionary<string, IDisposable> _outboundEventSubscriptions = new Dictionary<string, IDisposable>();

        public TestFunctionRpcService(IScriptEventManager eventManager, string workerId, TestLogger logger, string expectedLogMsg = "")
        {
            _eventManager = eventManager;
            _logger = logger;
            _workerId = workerId;
            _outboundEventSubscriptions.Add(workerId, _eventManager.OfType<OutboundGrpcEvent>()
                        .Where(evt => evt.WorkerId == workerId)
                        .Subscribe(evt =>
                        {
                            _logger.LogInformation(expectedLogMsg);
                        }));
        }

        public void PublishFunctionLoadResponseEvent(string functionId)
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };
            FunctionLoadResponse functionLoadResponse = new FunctionLoadResponse()
            {
                FunctionId = functionId,
                Result = statusResult
            };
            StreamingMessage responseMessage = new StreamingMessage()
            {
                FunctionLoadResponse = functionLoadResponse
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }

        public void PublishFunctionEnvironmentReloadResponseEvent()
        {
            FunctionEnvironmentReloadResponse relaodEnvResponse = GetTestFunctionEnvReloadResponse();
            StreamingMessage responseMessage = new StreamingMessage()
            {
                FunctionEnvironmentReloadResponse = relaodEnvResponse
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }

        public void PublishWorkerInitResponseEvent(IDictionary<string, string> capabilities = null)
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };

            WorkerInitResponse initResponse = new WorkerInitResponse()
            {
                Result = statusResult
            };

            if (capabilities != null)
            {
                initResponse.Capabilities.Add(capabilities);
            }

            StreamingMessage responseMessage = new StreamingMessage()
            {
                WorkerInitResponse = initResponse
            };

            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }

        public void PublishWorkerInitResponseEventWithSharedMemoryDataTransferCapability()
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };
            WorkerInitResponse initResponse = new WorkerInitResponse()
            {
                Result = statusResult
            };
            StreamingMessage responseMessage = new StreamingMessage()
            {
                WorkerInitResponse = initResponse
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }

        public void PublishSystemLogEvent(RpcLog.Types.Level inputLevel)
        {
            RpcLog rpcLog = new RpcLog()
            {
                LogCategory = RpcLog.Types.RpcLogCategory.System,
                Level = inputLevel,
                Message = "Random system log message",
            };

            StreamingMessage logMessage = new StreamingMessage()
            {
                RpcLog = rpcLog
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, logMessage));
        }

        public static FunctionEnvironmentReloadResponse GetTestFunctionEnvReloadResponse()
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };
            FunctionEnvironmentReloadResponse relaodEnvResponse = new FunctionEnvironmentReloadResponse()
            {
                Result = statusResult
            };
            return relaodEnvResponse;
        }

        public void PublishInvocationResponseEvent()
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };
            InvocationResponse invocationResponse = new InvocationResponse()
            {
                InvocationId = "TestInvocationId",
                Result = statusResult
            };
            StreamingMessage responseMessage = new StreamingMessage()
            {
                InvocationResponse = invocationResponse
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }

        public void PublishStartStreamEvent(string workerId)
        {
            StatusResult statusResult = new StatusResult()
            {
                Status = StatusResult.Types.Status.Success
            };
            StartStream startStream = new StartStream()
            {
                WorkerId = workerId
            };
            StreamingMessage responseMessage = new StreamingMessage()
            {
                StartStream = startStream
            };
            _eventManager.Publish(new InboundGrpcEvent(_workerId, responseMessage));
        }
    }
}