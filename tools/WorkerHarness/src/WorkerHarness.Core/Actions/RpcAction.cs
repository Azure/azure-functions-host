// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
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

        internal RpcAction(IValidatorFactory validatorFactory, 
            IMatcher matchService,
            IGrpcMessageProvider grpcMessageProvider, 
            RpcActionData actionData,
            IVariableObservable variableManager,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel)
        {
            _validatorFactory = validatorFactory;
            _matchService = matchService;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _variableManager = variableManager;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
        }

        // Type of action
        public string Type { get => _actionData.ActionType; }

        // Displayed name of the action
        public string Name { get => _actionData.ActionName; }

        // Execution timeout for an action
        public int Timeout { get => _actionData.Timeout; }

        public async Task<ActionResult> ExecuteAsync()
        {
            ActionResult actionResult = new(Type, Name);

            CancellationTokenSource tokenSource = new();

            StatusCode executionStatus = StatusCode.Success;

            // get a list of RpcActionMessage objects whose direction is outgoing
            IEnumerable<RpcActionMessage> outgoingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Outgoing, StringComparison.OrdinalIgnoreCase));

            // get a list of RpcActionMessage object whose direction is incoming
            IEnumerable<RpcActionMessage> incomingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Incoming, StringComparison.OrdinalIgnoreCase));

            // create a concurrent dictionary to map the rpcActionMessage to its status
            ConcurrentDictionary<RpcActionMessage, StatusCode> concurrentDictionary = new(); 
            foreach(RpcActionMessage msg in _actionData.Messages)
            {
                concurrentDictionary.TryAdd(msg, StatusCode.Timeout);
            }

            // wait for timeoutTask, SendGrpc, and ReceiveGrpc with cancellation token
            Task timeoutTask = Task.Run(() => Thread.Sleep(Timeout));
            Task<bool> sendTask = SendToGrpcAsync(outgoingRpcMessages, concurrentDictionary, tokenSource.Token);
            Task<bool> receiveTask = ReceiveFromGrpcAsync(incomingRpcMessages, concurrentDictionary, tokenSource.Token);

            Task finishedTask = await Task.WhenAny(timeoutTask, Task.WhenAll(sendTask, receiveTask));

            if (finishedTask.Id == timeoutTask.Id) // timeout occur
            {
                executionStatus = StatusCode.Timeout;
                tokenSource.Cancel();
            }
            else
            {
                executionStatus = await sendTask && await receiveTask ? StatusCode.Success : StatusCode.Error;
            }

            // use concurrentDictionary to write message to actionResult
            var dictionaryEnumerator = concurrentDictionary.GetEnumerator();
            while (dictionaryEnumerator.MoveNext())
            {
                var dictionaryEntry = dictionaryEnumerator.Current;
                string message = CreateActionResultMessage(dictionaryEntry.Key, dictionaryEntry.Value);
                actionResult.Messages.Add(message);
            }

            actionResult.Status = executionStatus;

            // clear all variables stored in _variableManager to get a clean state for the next action
            _variableManager.Clear();

            return actionResult;
        }

        private async Task<bool> ReceiveFromGrpcAsync(IEnumerable<RpcActionMessage> incomingRpcMessages, 
            ConcurrentDictionary<RpcActionMessage, StatusCode> concurrentDictionary,CancellationToken token)
        {
            RegisterExpressions(incomingRpcMessages);

            bool allMessagesValidated = true;

            IList<RpcActionMessage> unprocessedRpcMessages = incomingRpcMessages.ToList();

            while (!token.IsCancellationRequested && unprocessedRpcMessages.Any())
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

                    // update the status of the rpcActionMesage
                    StatusCode status = validated ? StatusCode.Success : StatusCode.Error;
                    if (!concurrentDictionary.TryUpdate(rpcActionMessage, status, StatusCode.Timeout))
                    {
                        throw new InvalidOperationException($"Unable to update the status of a {typeof(RpcActionMessage)} in a {typeof(ConcurrentDictionary<RpcActionMessage, StatusCode>)}");
                    }

                    allMessagesValidated = allMessagesValidated && validated;

                    // register grpcMsg as a variable in _variableManager
                    _variableManager.AddVariable(rpcActionMessage.Id, streamingMessage);

                }
            }

            return allMessagesValidated;
        }

        private void RegisterExpressions(IEnumerable<RpcActionMessage> incomingRpcMessages)
        {
            foreach (RpcActionMessage rpcActionMessage in incomingRpcMessages)
            {
                RegisterExpressionsInRpcActionMessage(rpcActionMessage);
            }
        }

        private async Task<bool> SendToGrpcAsync(IEnumerable<RpcActionMessage> outgoingRpcMessages, 
            ConcurrentDictionary<RpcActionMessage, StatusCode> concurrentDictionary, CancellationToken token)
        {
            bool allSent = true;

            var enumerator = outgoingRpcMessages.GetEnumerator();
            while (!token.IsCancellationRequested && enumerator.MoveNext())
            {
                var rpcActionMessage = enumerator.Current;
                var taskResult = await SendToGrpcAsync(rpcActionMessage, token);

                // update the status of rpcActionMessage in concurrentDictionary
                StatusCode status = taskResult ? StatusCode.Success : StatusCode.Error;
                if (!concurrentDictionary.TryUpdate(rpcActionMessage, status, StatusCode.Timeout))
                {
                    throw new InvalidOperationException($"Unable to update the status of a {typeof(RpcActionMessage)} in a {typeof(ConcurrentDictionary<RpcActionMessage, StatusCode>)}");
                }

                allSent = allSent && taskResult;
            }

            return allSent;
        }

        private static string CreateActionResultMessage(RpcActionMessage rpcActionMessage, StatusCode status)
        {
            string taskAction = string.Equals(rpcActionMessage.Direction, RpcActionMessageTypes.Incoming.ToString(), StringComparison.OrdinalIgnoreCase) ? "Validate" : "Send";
            string taskResultMessage = $"{taskAction} {rpcActionMessage.MessageType} message ... {status}";
            return taskResultMessage;
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

        private async Task<bool> SendToGrpcAsync(RpcActionMessage rpcActionMessage, CancellationToken cancellationToken)
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

            return true;
        }

    }
}
