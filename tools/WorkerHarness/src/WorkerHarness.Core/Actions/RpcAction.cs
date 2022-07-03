// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace WorkerHarness.Core
{
    internal class RpcAction : IAction
    {
        // _validatorManager is responsible for validating message.
        private readonly IValidatorFactory _validatorFactory;

        // _matchService decides if an incoming grpc message matches our criteria.
        private readonly IMatcher _matchService;

        // _grpcMessageProvider create the right StreamingMessage object.
        private readonly IGrpcMessageProvider _grpcMessageProvider;

        // _actionData encapsulates data for each action in the Scenario file.
        private readonly RpcActionData _actionData;

        // _variableManager evaluates all registered expressions once the variable values are available.
        private readonly IVariableObservable _variableManager;

        // _inboundChannel stores messages from Grpc Layer. These messages are yet matched or validated.
        private readonly Channel<StreamingMessage> _inboundChannel;

        // _outboundChannel stores messages that will be consumed by Grpc Layer and sent to the language worker.
        private readonly Channel<StreamingMessage> _outboundChannel;

        // _logger displays messages to user in the console
        private readonly ILogger<RpcAction> _logger;

        internal RpcAction(IValidatorFactory validatorFactory, 
            IMatcher matchService,
            IGrpcMessageProvider grpcMessageProvider, 
            RpcActionData actionData,
            IVariableObservable variableManager,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel,
            ILogger<RpcAction> logger)
        {
            _validatorFactory = validatorFactory;
            _matchService = matchService;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _variableManager = variableManager;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
            _logger = logger;
        }

        // Type of action
        public string Type { get => _actionData.ActionType; }

        // Displayed name of the action
        public string Name { get => _actionData.ActionName; }

        // Execution timeout for an action
        public int Timeout { get => _actionData.Timeout; }

        public async Task ExecuteAsync()
        {

            CancellationTokenSource tokenSource = new();

            StatusCode executionStatus = StatusCode.Success;

            // get a list of RpcActionMessage objects whose direction is outgoing
            IEnumerable<RpcActionMessage> outgoingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Outgoing, StringComparison.OrdinalIgnoreCase));

            // get a list of RpcActionMessage object whose direction is incoming
            IEnumerable<RpcActionMessage> incomingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Incoming, StringComparison.OrdinalIgnoreCase));

            // create a concurrent dictionary to map the rpcActionMessage to its status
            ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap = new(); 

            // wait for timeoutTask, SendGrpc, and ReceiveGrpc with cancellation token
            Task timeoutTask = Task.Delay(Timeout); // use Task.Delay
            Task<bool> sendTask = SendToGrpcAsync(outgoingRpcMessages, statusMap, tokenSource.Token);
            Task<bool> receiveTask = ReceiveFromGrpcAsync(incomingRpcMessages, statusMap, tokenSource.Token);

            Task finishedTask = await Task.WhenAny(timeoutTask, Task.WhenAll(sendTask, receiveTask));

            if (finishedTask.Id == timeoutTask.Id) // timeout occur
            {
                executionStatus = StatusCode.Failure;
                tokenSource.Cancel();
                await Task.WhenAll(sendTask, receiveTask);
            }
            else
            {
                executionStatus = await sendTask && await receiveTask ? StatusCode.Success : StatusCode.Failure;
            }
            
            DisplayRpcActionResult(executionStatus, statusMap);

            _variableManager.Clear();

        }

        private void DisplayRpcActionResult(StatusCode executionStatus, ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap)
        {
            if (executionStatus == StatusCode.Success)
            {
                _logger.LogInformation("Action \"{0}\" succeeds", _actionData.ActionName);
            }
            else
            {
                string userMessage = $"Action \"{_actionData.ActionName}\" fails";
                _logger.LogError(userMessage);

                var dictionaryEnumerator = statusMap.GetEnumerator();
                while (dictionaryEnumerator.MoveNext())
                {
                    RpcActionError error = dictionaryEnumerator.Current.Value;
                    string message = $"[{error.Type}]: {error.ConciseMessage}\n{error.Advice}";
                    _logger.LogError(message);
                }
            }

        }

        private async Task<bool> ReceiveFromGrpcAsync(IEnumerable<RpcActionMessage> incomingRpcMessages, 
            ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap, CancellationToken token)
        {
            RegisterExpressions(incomingRpcMessages);

            bool allValidated = true;

            IList<RpcActionMessage> unprocessedRpcMessages = incomingRpcMessages.ToList();

            while (!token.IsCancellationRequested && unprocessedRpcMessages.Any())
            {
                try
                {
                    StreamingMessage streamingMessage = await _inboundChannel.Reader.ReadAsync(token);

                    // filter through the unprocessedRpcMessages to see if there is any match
                    var matchedRpcMesseages = unprocessedRpcMessages
                        .Where(msg => string.Equals(streamingMessage.ContentCase.ToString(), msg.MessageType, StringComparison.OrdinalIgnoreCase))
                        .Where(msg => msg.DependenciesResolved() && _matchService.MatchAll(msg.MatchingCriteria, streamingMessage));

                    if (matchedRpcMesseages.Any())
                    {
                        RpcActionMessage rpcActionMessage = matchedRpcMesseages.First();
                        unprocessedRpcMessages.Remove(rpcActionMessage);

                        // validate streamingMessage against the validators in rpcActionMessage
                        bool validated = true;
                        foreach (var validationContext in rpcActionMessage.Validators)
                        {
                            string validatorType = validationContext.Type.ToLower();
                            IValidator validator = _validatorFactory.Create(validatorType);
                            bool validationResult = validator.Validate(validationContext, streamingMessage);

                            validated = validated && validationResult;
                        }

                        allValidated = allValidated && validated;

                        // update the status of the rpcActionMesage
                        if (!validated)
                        {
                            RpcActionError error = new()
                            {
                                Type = RpcErrorCode.Validation_Error,
                                ConciseMessage = string.Format(RpcErrorConstants.ValidationFailed, rpcActionMessage.MessageType),
                                Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorCode.Validation_Error, RpcErrorConstants.ValidationFailedLink)
                            };
                            error.VerboseMessage = $"{error.ConciseMessage}\n{streamingMessage.Serialize()}";

                            statusMap.AddOrUpdate(rpcActionMessage, error, (key, value) => error);
                        }

                        // register grpcMsg as a variable in _variableManager
                        _variableManager.AddVariable(rpcActionMessage.Id, streamingMessage);

                    }

                }
                catch (OperationCanceledException)
                {
                    allValidated = false;

                    foreach (RpcActionMessage rpcActionMessage in unprocessedRpcMessages)
                    {
                        RpcActionError error = new()
                        {
                            Type = RpcErrorCode.Message_Not_Received_Error,
                            ConciseMessage = string.Format(RpcErrorConstants.MessageNotReceived, rpcActionMessage.MessageType),
                            VerboseMessage = string.Format(RpcErrorConstants.MessageNotReceived, rpcActionMessage.MessageType),
                            Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorCode.Message_Not_Received_Error, RpcErrorConstants.MessageNotReceivedLink)
                        };

                        statusMap.AddOrUpdate(rpcActionMessage, error, (key, value) => error);
                    }

                    break;
                }
            }

            return allValidated;
        }

        private void RegisterExpressions(IEnumerable<RpcActionMessage> incomingRpcMessages)
        {
            foreach (RpcActionMessage rpcActionMessage in incomingRpcMessages)
            {
                RegisterExpressionsInRpcActionMessage(rpcActionMessage);
            }
        }

        private void RegisterExpressionsInRpcActionMessage(RpcActionMessage rpcActionMessage)
        {
            foreach (var matchingCriteria in rpcActionMessage.MatchingCriteria)
            {
                matchingCriteria.Query = VariableHelper.UpdateSingleDefaultVariableExpression(matchingCriteria.Query, rpcActionMessage.Id);

                matchingCriteria.ConstructExpression();

                _variableManager.Subscribe(matchingCriteria);
            }

            // in message.Validators, update any default variable '$.' to '$.{messageId}'
            foreach (var validator in rpcActionMessage.Validators)
            {
                validator.Query = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Query, rpcActionMessage.Id);

                validator.ConstructExpression();

                _variableManager.Subscribe(validator);
            }
        }

        private async Task<bool> SendToGrpcAsync(IEnumerable<RpcActionMessage> outgoingRpcMessages, 
            ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap, CancellationToken token)
        {
            bool allSent = true;

            var enumerator = outgoingRpcMessages.GetEnumerator();
            while (!token.IsCancellationRequested && enumerator.MoveNext())
            {
                var rpcActionMessage = enumerator.Current;
                var sent = await SendToGrpcAsync(rpcActionMessage, token);

                allSent = allSent && sent;

                if (!sent)
                {
                    RpcActionError error = new()
                    {
                        Type = RpcErrorCode.Message_Not_Sent_Error,
                        ConciseMessage = string.Format(RpcErrorConstants.MessageNotSent, rpcActionMessage.MessageType),
                        VerboseMessage = string.Format(RpcErrorConstants.MessageNotSent, rpcActionMessage.MessageType),
                        Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorCode.Message_Not_Sent_Error, RpcErrorConstants.MessageNotSentLink)
                    };

                    statusMap.AddOrUpdate(rpcActionMessage, error, (key, value) => error);
                }
                
            }

            return allSent;
        }

        private async Task<bool> SendToGrpcAsync(RpcActionMessage rpcActionMessage, CancellationToken cancellationToken)
        {
            bool succeeded;

            try
            {
                // create the appropriate Grpc message
                StreamingMessage streamingMessage = _grpcMessageProvider.Create(rpcActionMessage.MessageType, rpcActionMessage.Payload);

                // send it to grpc
                await _outboundChannel.Writer.WriteAsync(streamingMessage, cancellationToken);

                // set variables
                if (rpcActionMessage.SetVariables != null)
                {
                    // resolve "SetVariables" property
                    VariableHelper.ResolveVariableMap(rpcActionMessage.SetVariables, rpcActionMessage.Id, streamingMessage);

                    // add the variables inside "SetVariables" to VariableManager
                    foreach (KeyValuePair<string, string> variable in rpcActionMessage.SetVariables)
                    {
                        _variableManager.AddVariable(variable.Key, variable.Value);
                    }
                }

                _variableManager.AddVariable(rpcActionMessage.Id, streamingMessage);

                succeeded = true;
            }
            catch (OperationCanceledException)
            {
                succeeded = false;
            }

            return succeeded;
        }

    }
}
