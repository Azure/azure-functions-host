// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Diagnostics;
using WorkerHarness.Core.Matching;
using WorkerHarness.Core.Profiling;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Actions
{
    internal sealed class RpcAction : IAction, ICanStartProfiling, ICanStopProfiling
    {
        // _validatorManager is responsible for validating message.
        private readonly IValidatorFactory _validatorFactory;

        // _matchService decides if an incoming grpc message matches our criteria.
        private readonly IMessageMatcher _messageMatcher;

        // _grpcMessageProvider create the right StreamingMessage object.
        private readonly IStreamingMessageProvider _grpcMessageProvider;

        // _actionData encapsulates data for each action in the Scenario file.
        private readonly RpcActionData _actionData;

        // _inboundChannel stores messages from Grpc Layer. These messages are yet matched or validated.
        private readonly Channel<StreamingMessage> _inboundChannel;

        // _outboundChannel stores messages that will be consumed by Grpc Layer and sent to the language worker.
        private readonly Channel<StreamingMessage> _outboundChannel;

        // _logger logs info/error of the action execution
        private readonly ILogger<RpcAction> _logger;

        internal RpcAction(IValidatorFactory validatorFactory,
            IMessageMatcher matchService,
            IStreamingMessageProvider grpcMessageProvider,
            RpcActionData actionData,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel,
            ILogger<RpcAction> logger)
        {
            _validatorFactory = validatorFactory;
            _messageMatcher = matchService;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
            _logger = logger;
        }

        internal string Type { get => _actionData.ActionType; }

        internal string Name { get => _actionData.ActionName; }

        internal int Timeout { get => _actionData.Timeout; }

        internal bool WaitForUserInput => _actionData.WaitForUserInput;

        public bool StartProfiling => _actionData.StartProfiling;

        public bool StopProfiling => _actionData.StopProfiling;

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            if (!_actionData.RunInSilentMode)
            {
                _logger.LogInformation($"Executing the action: {Name} ...");
            }

            CancellationTokenSource tokenSource = new();

            StatusCode executionStatus;

            // get a list of RpcActionMessage objects whose direction is outgoing
            IEnumerable<RpcActionMessage> outgoingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Outgoing, StringComparison.OrdinalIgnoreCase));

            // get a list of RpcActionMessage object whose direction is incoming
            IEnumerable<RpcActionMessage> incomingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Incoming, StringComparison.OrdinalIgnoreCase));

            // wait for timeoutTask, SendGrpc, and ReceiveGrpc with cancellation token
            Task<bool> sendTask = SendToGrpcAsync(outgoingRpcMessages, executionContext, tokenSource.Token);
            Task<bool> receiveTask = ReceiveFromGrpcAsync(incomingRpcMessages, executionContext, tokenSource.Token);

            tokenSource.CancelAfter(Timeout);

            bool[] results = await Task.WhenAll(sendTask, receiveTask);

            if (results[0] && results[1])
            {
                executionStatus = StatusCode.Success;
            }
            else
            {
                executionStatus = StatusCode.Failure;
            }

            ActionResult actionResult = new() { Status = executionStatus };

            if (actionResult.Status == StatusCode.Success)
            {
                if (!_actionData.RunInSilentMode)
                {
                    var message = _actionData.SuccessMessage ?? "Success!";
                    if (!string.IsNullOrEmpty(message))
                    {
                        _logger.LogInformation(message);
                    }
                }
            }
            else
            {
                _logger.LogError($"Failure!");
            }

            tokenSource.Dispose();

            return actionResult;
        }

        private async Task<bool> ReceiveFromGrpcAsync(IEnumerable<RpcActionMessage> incomingRpcMessages,
            ExecutionContext executionContext, CancellationToken token)
        {
            RegisterExpressions(incomingRpcMessages, executionContext);

            bool allValidated = true;

            IList<RpcActionMessage> unprocessedRpcMessages = incomingRpcMessages.ToList();

            while (!token.IsCancellationRequested && unprocessedRpcMessages.Any())
            {
                try
                {
                    StreamingMessage streamingMessage = await _inboundChannel.Reader.ReadAsync(token);

                    if (streamingMessage.ContentCase == StreamingMessage.ContentOneofCase.InvocationResponse)
                    {
                        var httpResponse = streamingMessage.InvocationResponse.ReturnValue.ToObject();
                        if (httpResponse is HttpResponseData response)
                        {
                            HarnessEventSource.Log.ColdStartRequestStop(response.StatusCode);
                            string responseBodyString = response.Body == null ? string.Empty : System.Text.Encoding.UTF8.GetString(response.Body);
                            _logger.LogInformation($"HTTP trigger invocation response: {response.StatusCode} {Environment.NewLine}{responseBodyString}");
                        }
                    }

                    var matchedRpcMesseages = unprocessedRpcMessages
                        .Where(msg => _messageMatcher.Match(msg, streamingMessage));

                    if (matchedRpcMesseages.Any())
                    {
                        RpcActionMessage rpcActionMessage = matchedRpcMesseages.First();
                        unprocessedRpcMessages.Remove(rpcActionMessage);

                        bool validated = ValidateMessage(streamingMessage, rpcActionMessage, executionContext);
                        allValidated &= validated;

                        bool setVariablesResult = SetVariables(rpcActionMessage, streamingMessage, executionContext);
                        allValidated &= setVariablesResult;
                    }

                }
                catch (OperationCanceledException)
                {
                    allValidated = false;
                    break;
                }
            }

            foreach (RpcActionMessage rpcActionMessage in unprocessedRpcMessages)
            {
                LogMessageNotReceivedError(executionContext, rpcActionMessage);
            }

            return allValidated;
        }

        private async Task<bool> SendToGrpcAsync(IEnumerable<RpcActionMessage> outgoingRpcMessages,
            ExecutionContext executionContext, CancellationToken token)
        {
            bool allSent = true;

            var enumerator = outgoingRpcMessages.GetEnumerator();
            while (!token.IsCancellationRequested && enumerator.MoveNext())
            {
                var rpcActionMessage = enumerator.Current;
                var sent = await SendToGrpcAsync(rpcActionMessage, executionContext, token);

                allSent = allSent && sent;

                if (!sent)
                {
                    LogMessageNotSentError(executionContext, rpcActionMessage);
                }

            }

            return allSent;
        }

        private bool ValidateMessage(StreamingMessage streamingMessage, RpcActionMessage rpcActionMessage, ExecutionContext executionContext)
        {
            bool validated = true;
            foreach (var validationContext in rpcActionMessage.Validators)
            {
                bool validationResult = ValidateMessageAgainstContext(streamingMessage, validationContext);

                validated = validated && validationResult;
            }

            if (!validated)
            {
                LogAdviceForValidationError(executionContext, streamingMessage);
            }

            return validated;
        }

        private bool ValidateMessageAgainstContext(StreamingMessage streamingMessage, ValidationContext validationContext)
        {
            bool validationResult;

            try
            {
                string validatorType = validationContext.Type.ToLower();
                IValidator validator = _validatorFactory.Create(validatorType);

                validationResult = validator.Validate(validationContext, streamingMessage);

                if (!validationResult)
                {
                    string errorMessage = string.Format(ActionErrors.ValidationErrorMessage, validationContext.Query, validationContext.Expected);
                    _logger.LogError($"{ActionErrorCode.ValidationError}: {errorMessage}");
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogError($"{ActionErrorCode.ValidationError}: {ex.Message}");

                validationResult = false;
            }

            return validationResult;
        }

        private async Task<bool> SendToGrpcAsync(RpcActionMessage rpcActionMessage, ExecutionContext executionContext, CancellationToken cancellationToken)
        {
            bool succeeded;

            try
            {
                bool created = _grpcMessageProvider.TryCreate(out StreamingMessage streamingMessage,
                    rpcActionMessage.MessageType, rpcActionMessage.Payload, executionContext.GlobalVariables);

                if (created)
                {
                    await _outboundChannel.Writer.WriteAsync(streamingMessage, cancellationToken);

                    bool setVariablesResult = SetVariables(rpcActionMessage, streamingMessage, executionContext);
                    succeeded = setVariablesResult;
                }
                else
                {
                    succeeded = false;
                }
            }
            catch (OperationCanceledException)
            {
                succeeded = false;
            }

            return succeeded;
        }

        private void LogAdviceForValidationError(ExecutionContext executionContext, StreamingMessage streamingMessage)
        {
            if (executionContext.DisplayVerboseError)
            {
                _logger.LogError($"The failed StreamingMessage is \n {streamingMessage.Serialize()}");
            }
            else
            {
                _logger.LogError(ActionErrors.DisplayVerboseErrorAdvice);
            }

            _logger.LogError(ActionErrors.GeneralErrorAdvice);
        }

        private void LogMessageNotReceivedError(ExecutionContext executionContext, RpcActionMessage rpcActionMessage)
        {
            _logger.LogError($"{ActionErrorCode.MessageNotReceivedError}: {string.Format(ActionErrors.MessageNotReceiveErrorMessage, rpcActionMessage.MessageType)}");

            if (executionContext.DisplayVerboseError)
            {
                if (rpcActionMessage.MatchingCriteria.Any())
                {
                    _logger.LogError($"The matching criteria are: {rpcActionMessage.MatchingCriteria.Serialize()}");
                }
            }
            else
            {
                _logger.LogError(ActionErrors.DisplayVerboseErrorAdvice);
            }

            _logger.LogError(ActionErrors.GeneralErrorAdvice);
        }

        private void LogMessageNotSentError(ExecutionContext executionContext, RpcActionMessage rpcActionMessage)
        {
            _logger.LogError($"{ActionErrorCode.MessageNotSentError}: {string.Format(ActionErrors.MessageNotSentErrorMessage, rpcActionMessage.MessageType)}");
            _logger.LogError(ActionErrors.MessageNotSentErrorAdvice);

            if (executionContext.DisplayVerboseError)
            {
                if (rpcActionMessage.Payload != null)
                {
                    _logger.LogError($"The payload is: {rpcActionMessage.Payload.Serialize()}");
                }
            }
            else
            {
                _logger.LogError(ActionErrors.DisplayVerboseErrorAdvice);
            }

            _logger.LogError(ActionErrors.GeneralErrorAdvice);
        }

        private void RegisterExpressions(IEnumerable<RpcActionMessage> incomingRpcMessages, ExecutionContext executionContext)
        {
            foreach (RpcActionMessage rpcActionMessage in incomingRpcMessages)
            {
                RegisterExpressions(rpcActionMessage, executionContext);
            }
        }

        private void RegisterExpressions(RpcActionMessage rpcActionMessage, ExecutionContext executionContext)
        {
            foreach (var matchingCriteria in rpcActionMessage.MatchingCriteria)
            {
                matchingCriteria.ConstructExpression();

                executionContext.GlobalVariables.Subscribe(matchingCriteria);
            }

            foreach (var validator in rpcActionMessage.Validators)
            {
                validator.ConstructExpression();

                executionContext.GlobalVariables.Subscribe(validator);
            }
        }

        private bool SetVariables(RpcActionMessage rpcActionMessage, StreamingMessage streamingMessage, ExecutionContext executionContext)
        {
            if (rpcActionMessage.SetVariables == null)
            {
                return true;
            }

            IDictionary<string, string> variableSettings = rpcActionMessage.SetVariables;
            foreach (KeyValuePair<string, string> setting in variableSettings)
            {
                string variableName = setting.Key;
                string query = setting.Value;

                try
                {
                    executionContext.GlobalVariables.AddVariable(variableName, streamingMessage.Query(query));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogCritical("Scenario input error: \"SetVariables\" property contains invalid query. {0}", ex.Message);
                    _logger.LogCritical("The invalid query is \"{0}\"", query);
                    _logger.LogCritical("The queried message is {0}", streamingMessage.Serialize());

                    return false;
                }
            }

            return true;
        }

    }
}
