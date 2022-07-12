// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Collections.Concurrent;
using System.Threading.Channels;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.Matching;
using WorkerHarness.Core.Validators;
using WorkerHarness.Core.Variables;

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
            CancellationTokenSource tokenSource = new();

            StatusCode executionStatus;

            // get a list of RpcActionMessage objects whose direction is outgoing
            IEnumerable<RpcActionMessage> outgoingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Outgoing, StringComparison.OrdinalIgnoreCase));

            // get a list of RpcActionMessage object whose direction is incoming
            IEnumerable<RpcActionMessage> incomingRpcMessages = _actionData.Messages
                .Where(msg => string.Equals(msg.Direction, RpcActionMessageTypes.Incoming, StringComparison.OrdinalIgnoreCase));

            // create a concurrent dictionary to map the rpcActionMessage to its status
            ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap = new(); 

            // wait for timeoutTask, SendGrpc, and ReceiveGrpc with cancellation token
            Task<bool> sendTask = SendToGrpcAsync(outgoingRpcMessages, statusMap, tokenSource.Token);
            Task<bool> receiveTask = ReceiveFromGrpcAsync(incomingRpcMessages, statusMap, tokenSource.Token);

            //tokenSource.CancelAfter(Timeout);

            //bool[] results = await Task.WhenAll(sendTask, receiveTask);

            //if (results[0] && results[1])
            //{
            //    executionStatus = StatusCode.Success;
            //} 
            //else
            //{
            //    executionStatus = StatusCode.Failure;
            //}

            Task timeoutTask = Task.Delay(Timeout);

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

            ActionResult actionResult = CreateActionResult(executionStatus, statusMap);

            _variableManager.Clear();

            tokenSource.Dispose();

            return actionResult;
        }

        private ActionResult CreateActionResult(StatusCode executionStatus, ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap)
        {
            ActionResult result = new()
            {
                Status = executionStatus,
                Message = executionStatus == StatusCode.Success ? $"Action \"{Name}\" succeeds" : $"Action \"{Name}\" fails"
            };

            var dictionaryEnumerator = statusMap.GetEnumerator();
            while (dictionaryEnumerator.MoveNext())
            {
                RpcActionError error = dictionaryEnumerator.Current.Value;
                result.ErrorMessages.Add($"[{error.Type}]: {error.ConciseMessage}\n{error.Advice}");
                result.VerboseErrorMessages.Add($"[{error.Type}]: {error.VerboseMessage}\n{error.Advice}");
            }

            return result;
        }

        private async Task<bool> ReceiveFromGrpcAsync(IEnumerable<RpcActionMessage> incomingRpcMessages, 
            ConcurrentDictionary<RpcActionMessage, RpcActionError> statusMap, CancellationToken token)
        {
            RegisterExpressions(incomingRpcMessages);

            bool allValidated = true;

            IList<RpcActionMessage> unprocessedRpcMessages = incomingRpcMessages.ToList();

            //var timeout = Task.Run(() => Task.Delay(Timeout).Wait());

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
                                VerboseMessage = string.Format(RpcErrorConstants.ValidationFailedVerbose, rpcActionMessage.MessageType, streamingMessage.Serialize()),
                                Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorConstants.ValidationFailedLink)
                            };

                            statusMap.AddOrUpdate(rpcActionMessage, error, (key, value) => error);
                        }

                        // setVariables
                        SetVariables(rpcActionMessage, streamingMessage);

                        // register grpcMsg as a variable in _variableManager
                        _variableManager.AddVariable(rpcActionMessage.Id, streamingMessage);

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
                RpcActionError error = new()
                {
                    Type = RpcErrorCode.Message_Not_Received_Error,
                    ConciseMessage = string.Format(RpcErrorConstants.MessageNotReceived, rpcActionMessage.MessageType),
                    VerboseMessage = string.Format(RpcErrorConstants.MessageNotReceivedVerbose, rpcActionMessage.MessageType, rpcActionMessage.Serialize()),
                    Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorConstants.MessageNotReceivedLink)
                };

                statusMap.AddOrUpdate(rpcActionMessage, error, (key, value) => error);
            }

            return allValidated;
        }

        private void RegisterExpressions(IEnumerable<RpcActionMessage> incomingRpcMessages)
        {
            foreach (RpcActionMessage rpcActionMessage in incomingRpcMessages)
            {
                RegisterExpressions(rpcActionMessage);
            }
        }

        private void RegisterExpressions(RpcActionMessage rpcActionMessage)
        {
            foreach (var matchingCriteria in rpcActionMessage.MatchingCriteria)
            {
                //matchingCriteria.Query = VariableHelper.UpdateSingleDefaultVariableExpression(matchingCriteria.Query, rpcActionMessage.Id);

                matchingCriteria.ConstructExpression();

                _variableManager.Subscribe(matchingCriteria);
            }

            // in message.Validators, update any default variable '$.' to '$.{messageId}'
            foreach (var validator in rpcActionMessage.Validators)
            {
                //validator.Query = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Query, rpcActionMessage.Id);

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
                        VerboseMessage = string.Format(RpcErrorConstants.MessageNotSentVerbose, rpcActionMessage.MessageType, Timeout),
                        Advice = string.Format(RpcErrorConstants.GeneralErrorAdvice, RpcErrorConstants.MessageNotSentLink)
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
                SetVariables(rpcActionMessage, streamingMessage);

                _variableManager.AddVariable(rpcActionMessage.Id, streamingMessage);

                succeeded = true;
            }
            catch (OperationCanceledException)
            {
                succeeded = false;
            }

            return succeeded;
        }

        private void SetVariables(RpcActionMessage rpcActionMessage, StreamingMessage streamingMessage)
        {
            if (rpcActionMessage.SetVariables == null)
            {
                return;
            }

            IDictionary<string, string> variableSettings = rpcActionMessage.SetVariables!;
            foreach (KeyValuePair<string, string> setting in variableSettings)
            {
                string variableName = setting.Key;
                string query = setting.Value;

                try
                {
                    _variableManager.AddVariable(variableName, streamingMessage.Query(query));
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException($"Scenario input error: \"SetVariables\" property contains invalid query", ex);
                }
            }
        }

    }
}
