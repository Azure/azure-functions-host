// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core.Actions
{
    internal class TerminateAction : IAction
    {
        internal string Type => ActionTypes.Terminate;
        internal int GracePeriodInSeconds => _gracePeriodInSeconds;

        private readonly int _gracePeriodInSeconds;
        private readonly Channel<StreamingMessage> _outboundChannel;
        private readonly IStreamingMessageProvider _streamingMessageProvider;
        private readonly ILogger<TerminateAction> _logger;

        internal TerminateAction(int gracePeriodInSeconds, 
            Channel<StreamingMessage> outboundChannel, 
            IStreamingMessageProvider streamingMessageProvider, 
            ILogger<TerminateAction> logger)
        {
            _gracePeriodInSeconds = gracePeriodInSeconds;
            _outboundChannel = outboundChannel;
            _streamingMessageProvider = streamingMessageProvider;
            _logger = logger;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext execuationContext)
        {
            _logger.LogInformation("Starting terminate action: allowing worker {0} seconds to shutdown", _gracePeriodInSeconds);

            // Create an ActionResult
            ActionResult actionResult = new();

            // Create a WorkerTerminate StreamingMessage
            string messageType = StreamingMessage.ContentOneofCase.WorkerTerminate.ToString();
            JsonNode payload = new JsonObject
            {
                ["GracePeriod"] = new JsonObject
                {
                    ["Seconds"] = _gracePeriodInSeconds
                }
            };

            StreamingMessage streamingMessage = _streamingMessageProvider.Create(messageType, payload);

            // Send to Worker
            await _outboundChannel.Writer.WriteAsync(streamingMessage);

            // Wait for WorkerProcess to exit
            IWorkerProcess workerProcess = execuationContext.WorkerProcess;
            workerProcess.WaitForProcessExit(_gracePeriodInSeconds * 1000);

            if (workerProcess.HasExited)
            {
                actionResult.Status = StatusCode.Success;
                _logger.LogInformation("Worker process has exited within {0} seconds", _gracePeriodInSeconds);
            }
            else
            {
                actionResult.Status = StatusCode.Failure;

                string errorMessage = string.Format(ActionErrors.WorkerNotExitMessage, _gracePeriodInSeconds);
                _logger.LogError("[{0}]: {1}", ActionErrorCode.Worker_Not_Exit_Error, errorMessage);
                _logger.LogError("{0}", ActionErrors.WorkerNotExitAdvice);
                _logger.LogError("For more information on the error, please visit {0}", ActionErrors.WorkerNotExitLink);
            }

            return actionResult;
        }
    }
}
