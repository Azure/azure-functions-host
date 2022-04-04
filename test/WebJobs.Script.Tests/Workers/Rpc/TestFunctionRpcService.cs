// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class TestFunctionRpcService
    {
        private IScriptEventManager _eventManager;
        private ILogger _logger;
        private string _workerId;
        private IDictionary<string, IDisposable> _outboundEventSubscriptions = new Dictionary<string, IDisposable>();
        private ChannelWriter<InboundGrpcEvent> _inboundWriter;

        public TestFunctionRpcService(IScriptEventManager eventManager, string workerId, TestLogger logger, string expectedLogMsg = "")
        {
            _eventManager = eventManager;
            _logger = logger;
            _workerId = workerId;
            if (eventManager.TryGetGrpcChannels(workerId, out var inbound, out var outbound))
            {
                _ = ListenAsync(outbound.Reader, expectedLogMsg);
                _inboundWriter = inbound.Writer;
            }
        }

        private async Task ListenAsync(ChannelReader<OutboundGrpcEvent> source, string expectedLogMsg)
        {
            await Task.Yield(); // free up caller
            try
            {
                while (await source.WaitToReadAsync())
                {
                    while (source.TryRead(out var evt))
                    {
                        _logger.LogDebug("[service] received {0}, {1}", evt.WorkerId, evt.MessageType);
                        _logger.LogInformation(expectedLogMsg);
                    }
                }
            }
            catch
            {
            }
        }

        private ValueTask WriteAsync(StreamingMessage message)
            => _inboundWriter is null ? default
            : _inboundWriter.WriteAsync(new InboundGrpcEvent(_workerId, message));

        private void Write(StreamingMessage message)
        {
            if (_inboundWriter is null)
            {
                _logger.LogDebug("[service] no writer for {0}, {1}", _workerId, message.ContentCase);
                return;
            }
            var evt = new InboundGrpcEvent(_workerId, message);
            _logger.LogDebug("[service] sending {0}, {1}", evt.WorkerId, evt.MessageType);
            if (_inboundWriter.TryWrite(evt))
            {
                return;
            }
            var vt = _inboundWriter.WriteAsync(evt);
            if (vt.IsCompleted)
            {
                vt.GetAwaiter().GetResult();
            }
            else
            {
                _ = ObserveEventually(vt);
            }
            static async Task ObserveEventually(ValueTask valueTask)
            {
                try
                {
                    await valueTask;
                }
                catch
                {
                    // log somewhere?
                }
            }
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
            Write(responseMessage);
        }

        public void PublishFunctionLoadResponsesEvent(List<string> functionIds, StatusResult statusResult)
        {
            FunctionLoadResponseCollection functionLoadResponseCollection = new FunctionLoadResponseCollection();

            foreach (string functionId in functionIds)
            {
                FunctionLoadResponse functionLoadResponse = new FunctionLoadResponse()
                {
                    FunctionId = functionId,
                    Result = statusResult
                };

                functionLoadResponseCollection.FunctionLoadResponses.Add(functionLoadResponse);
            }

            StreamingMessage responseMessage = new StreamingMessage()
            {
                FunctionLoadResponseCollection = functionLoadResponseCollection
            };
            Write(responseMessage);
        }

        public void PublishFunctionEnvironmentReloadResponseEvent()
        {
            FunctionEnvironmentReloadResponse relaodEnvResponse = GetTestFunctionEnvReloadResponse();
            StreamingMessage responseMessage = new StreamingMessage()
            {
                FunctionEnvironmentReloadResponse = relaodEnvResponse
            };
            Write(responseMessage);
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

            Write(responseMessage);
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
            Write(responseMessage);
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
            Write(logMessage);
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
            Write(responseMessage);
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
            Write(responseMessage);
        }

        public void PublishWorkerMetadataResponse(string workerId, string functionId, IEnumerable<FunctionMetadata> functionMetadata, bool successful, bool useDefaultMetadataIndexing = false)
        {
            StatusResult statusResult = new StatusResult();
            if (successful)
            {
                statusResult.Status = StatusResult.Types.Status.Success;
            }
            else
            {
                statusResult.Status = StatusResult.Types.Status.Failure;
            }

            FunctionMetadataResponse overallResponse = new FunctionMetadataResponse();
            overallResponse.UseDefaultMetadataIndexing = useDefaultMetadataIndexing;

            foreach (FunctionMetadata response in functionMetadata)
            {
                RpcFunctionMetadata indexingResponse = new RpcFunctionMetadata()
                {
                    Name = response.Name,
                    Language = response.Language,
                    Status = statusResult,
                    FunctionId = functionId
                };

                overallResponse.FunctionMetadataResults.Add(indexingResponse);
            }

            StreamingMessage responseMessage = new StreamingMessage()
            {
                FunctionMetadataResponse = overallResponse
            };
            Write(responseMessage);
        }
    }
}