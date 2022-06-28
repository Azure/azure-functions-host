// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
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
            ActionResult result = new(Type, Name);

            var numberOfProcessedMessages = 0;

            CancellationTokenSource tokenSource = new();

            Task timeoutTask = Task.Run(() => Thread.Sleep(Timeout));

            StatusCode executionStatus = StatusCode.Success;

            foreach (RpcActionMessage rpcActionMessage in _actionData.Messages)
            {
                Task<bool> task = CreateGrpcTask(rpcActionMessage, tokenSource.Token);
                
                Task finishedTask = await Task.WhenAny(new Task[] { timeoutTask, task });

                if (finishedTask.Exception != null)
                {
                    throw finishedTask.Exception;
                }

                if (finishedTask.Id == timeoutTask.Id)
                {
                    tokenSource.Cancel();

                    executionStatus = StatusCode.Timeout;
                    result.Messages.Add(CreateGrpcTaskMessage(rpcActionMessage, executionStatus));
                    result.Messages.Add($"Processed {numberOfProcessedMessages}/{_actionData.Messages.Count()} messages");

                    break;
                }

                if (!await task)
                {
                    executionStatus = StatusCode.Error;
                    result.Messages.Add(CreateGrpcTaskMessage(rpcActionMessage, StatusCode.Error));
                }
                else
                {
                    result.Messages.Add(CreateGrpcTaskMessage(rpcActionMessage, StatusCode.Success));
                }

                numberOfProcessedMessages++;

            }

            // clear all variables stored in _variableManager to get a clean state for the next action
            _variableManager.Clear();

            // if timeout occurs, and not all messages in _actionData.Messages are processed, then write failure
            result.Status = executionStatus;

            return result;
        }

        private static string CreateGrpcTaskMessage(RpcActionMessage rpcActionMessage, StatusCode status)
        {
            string taskAction = string.Equals(rpcActionMessage.Direction, RpcActionMessageTypes.Incoming.ToString(), StringComparison.OrdinalIgnoreCase) ? "Validate" : "Send";
            string taskResultMessage = $"{taskAction} {rpcActionMessage.MessageType} message ... {status}";
            return taskResultMessage;
        }

        private Task<bool> CreateGrpcTask(RpcActionMessage rpcActionMessage, CancellationToken cancellationToken)
        {
            Task<bool> task;

            if (string.Equals(rpcActionMessage.Direction, RpcActionMessageTypes.Outgoing, StringComparison.OrdinalIgnoreCase))
            {
                task = SendToGrpcAsync(rpcActionMessage, cancellationToken);
            }
            else if (string.Equals(rpcActionMessage.Direction, RpcActionMessageTypes.Incoming, StringComparison.OrdinalIgnoreCase))
            {
                task = ReceiveFromGrpcAsync(rpcActionMessage, cancellationToken);
            }
            else
            {
                throw new InvalidDataException($"Invalid Rpc message's direction {rpcActionMessage.Direction}");
            }

            return task;
        }

        private async Task<bool> ReceiveFromGrpcAsync(RpcActionMessage rpcActionMessage, CancellationToken cancellationToken)
        {
            RegisterExpressionsInRpcActionMessage(rpcActionMessage);

            bool allValidated = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                StreamingMessage grpcMsg = await _inboundChannel.Reader.ReadAsync(cancellationToken);

                bool messageTypeMatched() => string.Equals(rpcActionMessage.MessageType, grpcMsg.ContentCase.ToString(), StringComparison.OrdinalIgnoreCase);
                bool matchingCriteriaMet() => rpcActionMessage.DependenciesResolved() && _matchService.MatchAll(rpcActionMessage.MatchingCriteria, grpcMsg);

                if (messageTypeMatched() && matchingCriteriaMet())
                {
                    // validate the match
                    foreach (var validationContext in rpcActionMessage.Validators)
                    {
                        string validatorType = validationContext.Type.ToLower();
                        IValidator validator = _validatorFactory.Create(validatorType);
                        bool validated = validator.Validate(validationContext, grpcMsg);

                        allValidated = allValidated && validated;
                    }

                    // register grpcMsg as a variable in _variableManager
                    _variableManager.AddVariable(rpcActionMessage.Id, grpcMsg);

                    break;
                }
            }

            return allValidated;
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
