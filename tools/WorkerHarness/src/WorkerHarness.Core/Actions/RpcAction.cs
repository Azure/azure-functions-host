using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class RpcAction : IAction
    {
        // _validatorManager is responsible for validating message.
        private readonly IValidatorFactory _validatorFactory;

        // _matchService decides if an incoming grpc message matches our criteria.
        private readonly IMatch _matchService;

        // _grpcMessageProvider create the right StreamingMessage object.
        private readonly IGrpcMessageProvider _grpcMessageProvider;

        // _actionData encapsulates data for each action in the Scenario file.
        private readonly RpcActionData _actionData;

        // _variableManager evaluates all registered expressions once the variable values are available.
        private readonly IVariableManager _variableManager;

        // _inboundChannel stores messages from Grpc Layer. These messages are yet matched or validated.
        private readonly Channel<StreamingMessage> _inboundChannel;

        // _outboundChannel stores messages that will be consumed by Grpc Layer and sent to the language worker.
        private readonly Channel<StreamingMessage> _outboundChannel;

        // _actionWriter writes action execution results to appropriate medium.
        private readonly IActionWriter _actionWriter;

        internal RpcAction(IValidatorFactory validatorFactory, 
            IMatch matchService,
            IGrpcMessageProvider grpcMessageProvider, 
            RpcActionData actionData,
            IVariableManager variableManager,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel,
            IActionWriter actionWriter
        )
        {
            _validatorFactory = validatorFactory;
            _matchService = matchService;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _variableManager = variableManager;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
            _actionWriter = actionWriter;
        }

        // Type of action, type "Default" in this case
        public string Type { get => _actionData.ActionType; }

        // Displayed name of the action
        public string Name { get => _actionData.ActionName; }

        // Execution timeout for an action
        public int Timeout { get => _actionData.Timeout; }

        // Placeholder that stores StreamingMessage to be sent to Grpc Layer
        private readonly IList<StreamingMessage> _grpcOutgoingMessages = new List<StreamingMessage>();

        // Placeholder that stores IncomingMessage to be matched and validated against messages from Grpc Layer.
        private readonly IList<IncomingMessage> _unmatchedMessages = new List<IncomingMessage>();

        public async Task ExecuteAsync()
        {
            //_actionWriter.WriteActionName(Name);

            //// create grpc messages to send to Grpc
            //ProcessOutgoingMessages();

            //// process incoming message's match criteria and validators
            //ProcessIncomingMessages();

            //// push grpc outgoing message to the _channel
            //await SendToGrpcAsync();

            //// listen and process any StreamingMessage from _channel
            //await ReceiveFromGrpcAsync();

            //// clear all variables stored in _variableManager to get a clean state for the next action
            //_variableManager.Clear();

            //_actionWriter.WriteActionEnding();

            throw new NotImplementedException();
        }

        private async Task ReceiveFromGrpcAsync()
        {
            var timeout = Task.Run(() => Thread.Sleep(Timeout));
            while (!timeout.IsCompleted && _unmatchedMessages.Any())
            {
                StreamingMessage grpcMsg = await _inboundChannel.Reader.ReadAsync();

                // filter _unvalidatedMessages for those with same ContentCase and fullfills the matching criteria
                IEnumerable<IncomingMessage> matches = _unmatchedMessages.Where(msg => msg.ContentCase.ToLower() == grpcMsg.ContentCase.ToString().ToLower())
                    .Where(msg => msg.DependenciesResolved() && _matchService.MatchAll(msg.Match, grpcMsg));

                if (matches.Any())
                {
                    IncomingMessage matchMsg = matches.First();
                    _unmatchedMessages.Remove(matchMsg);

                    foreach (var matchingCriteria in matchMsg.Match)
                    {
                        _actionWriter.Match.Add(matchingCriteria);
                    }

                    // validate the match
                    foreach (var validationContext in matchMsg.Validators)
                    {
                        string validatorType = validationContext.Type.ToLower();
                        IValidator validator = _validatorFactory.Create(validatorType);
                        bool validated = validator.Validate(validationContext, grpcMsg);

                        _actionWriter.ValidationResults.Add(validationContext, validated);
                    }

                    _actionWriter.WriteMatchedMessage(grpcMsg);

                    // register grpcMsg as a variable in _variableManager
                    _variableManager.AddVariable(matchMsg.Id, grpcMsg);

                }
                else
                {
                    await _inboundChannel.Writer.WriteAsync(grpcMsg);
                }
            }

            // if there are any unmatched messages, write them
            if (_unmatchedMessages.Any())
            {
                foreach (IncomingMessage msg in _unmatchedMessages)
                {
                    _actionWriter.WriteUnmatchedMessages(msg);
                }
            }

        }

        private async Task SendToGrpcAsync()
        {
            foreach (StreamingMessage message in _grpcOutgoingMessages)
            {
                await _outboundChannel.Writer.WriteAsync(message);
                _actionWriter.WriteSentMessage(message);
            }
        }

        /// <summary>
        /// Create StreamingMessage objects for each action in the scenario file.
        /// Add them to the _grpcOutgoingMessages to be sent to Grpc Service layer later.
        /// If users set any variables, resolve those variables.
        /// 
        /// </summary>
        /// <exception cref="NullReferenceException"></exception>
        //private void ProcessOutgoingMessages()
        //{

        //    foreach (OutgoingMessage message in _actionData.OutgoingMessages)
        //    {
        //        // create a StreamingMessage that will be sent to a language worker
        //        StreamingMessage streamingMessage = _grpcMessageProvider.Create(message.ContentCase, message.Content);

        //        // resolve "SetVariables" property
        //        VariableHelper.ResolveVariableMap(message.SetVariables, message.Id, streamingMessage);

        //        // add the variables inside "SetVariables" to VariableManager
        //        if (message.SetVariables != null)
        //        {
        //            foreach (KeyValuePair<string, string> variable in message.SetVariables)
        //            {
        //                _variableManager.AddVariable(variable.Key, variable.Value);
        //            }
        //        }

        //        _variableManager.AddVariable(message.Id, streamingMessage);

        //        _grpcOutgoingMessages.Add(streamingMessage);
        //    }
        //}

        /// <summary>
        /// Register incoming messages that are to be validated against actual grpc messages.
        /// 
        /// Subscribe variable expressions (if any) mentioned in the scenario file.
        /// Variable expressions are allowed in:
        ///     - Expected field in the Match property: this feature allows user to identify an incoming message based on
        ///     the property of another message, which enable dependency between messages
        ///     - Expected field in the Validators property: this feature allows user to validate an incoming message based on
        ///     the property of another message.
        ///     
        /// </summary>
        //private void ProcessIncomingMessages()
        //{
        //    //JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
        //    foreach (IncomingMessage message in _actionData.IncomingMessages)
        //    {
        //        // in message.Match, update any default variable '$.' to '$.{messageId}'
        //        foreach (var matchingCriteria in message.Match)
        //        {
        //            matchingCriteria.Query = VariableHelper.UpdateSingleDefaultVariableExpression(matchingCriteria.Query, message.Id);

        //            matchingCriteria.ConstructExpression();

        //            _variableManager.Subscribe(matchingCriteria);
        //        }

        //        // in message.Validators, update any default variable '$.' to '$.{messageId}'
        //        foreach (var validator in message.Validators)
        //        {
        //            //validator.Query = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Query, message.Id);
        //            validator.Query = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Query, message.Id);

        //            validator.ConstructExpression();

        //            _variableManager.Subscribe(validator);
        //        }

        //        _unmatchedMessages.Add(message);
        //    }
        //}
    }
}
