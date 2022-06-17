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
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core
{
    internal class DefaultAction : IAction
    {
        // _validatorManager is responsible for validating message. TODO: implement the validation functionality
        private IValidatorFactory _validatorFactory;

        // _grpcMessageProvider create the right StreamingMessage object
        private IGrpcMessageProvider _grpcMessageProvider;

        // _actionData encapsulates data for each action in the Scenario file.
        private DefaultActionData _actionData;

        // _variableManager evaluates all registered expressions once the variable values are available.
        private IVariableManager _variableManager;

        private Channel<StreamingMessage> _inboundChannel;
        private Channel<StreamingMessage> _outboundChannel;

        private IActionWriter _actionWriter;

        internal DefaultAction(IValidatorFactory validatorFactory, 
            IGrpcMessageProvider grpcMessageProvider, 
            DefaultActionData actionData,
            IVariableManager variableManager,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel,
            IActionWriter actionWriter
        )
        {
            _validatorFactory = validatorFactory;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _variableManager = variableManager;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
            _actionWriter = actionWriter;
        }

        // Type of action, type "Default" in this case
        public string? Type { get => _actionData.Type; }

        // Displayed name of the action
        public string? Name { get => _actionData.Name; }

        // Execution timeout for an action
        public int Timeout { get => _actionData.Timeout; }

        // Placeholder that stores StreamingMessage to be sent to Grpc Layer
        private IList<StreamingMessage> _grpcOutgoingMessages = new List<StreamingMessage>();

        // Placeholder that stores IncomingMessage to be matched and validated against messages from Grpc Layer.
        private IList<IncomingMessage> _unmatchedMessages = new List<IncomingMessage>();

        public async Task ExecuteAsync()
        {
            _actionWriter.WriteActionName(_actionData.Name ?? string.Empty);

            // create grpc messages to send to Grpc
            ProcessOutgoingMessages();

            // process incoming message's match criteria and validators
            ProcessIncomingMessages();

            // push grpc outgoing message to the _channel
            await SendToGrpcAsync();

            // listen and process any StreamingMessage from _channel
            await ReceiveFromGrpcAsync();

            // clear all variables stored in _variableManager to get a clean state for the next action
            _variableManager.Clear();

            _actionWriter.WriteActionEnding();
        }

        private async Task ReceiveFromGrpcAsync()
        {
            //JsonSerializerOptions options = new() { WriteIndented = true };
            //options.Converters.Add(new JsonStringEnumConverter());

            var timeout = Task.Run(() => Thread.Sleep(Timeout));
            while (!timeout.IsCompleted && _unmatchedMessages.Any())
            {
                StreamingMessage grpcMsg = await _inboundChannel.Reader.ReadAsync();
                // iterate through _unvalidatedMessages and select the first one which has the same ContentCase
                IEnumerable<IncomingMessage> matches = _unmatchedMessages.Where(msg => msg.ContentCase != null && msg.ContentCase.ToLower() == grpcMsg.ContentCase.ToString().ToLower())
                    // filter those that fulfills the match criteria
                    .Where(msg => msg.Match != null && msg.Match.ExpectedExpression != null && msg.Match.ExpectedExpression.Resolved && MatchHelper.Matched(msg.Match, grpcMsg));

                if (matches.Any())
                {
                    IncomingMessage match = matches.First();
                    _unmatchedMessages.Remove(match);

                    _actionWriter.Match.Add(match.Match!);

                    // validate the match
                    if (match.Validators != null)
                    {
                        foreach (ValidationContext validationContext in match.Validators)
                        {
                            string validatorType = validationContext.Type != null ? validationContext.Type.ToLower() : throw new MissingFieldException($"Missing validator type");
                            IValidator validator = _validatorFactory.Create(validatorType);
                            bool validated = validator.Validate(validationContext, grpcMsg);

                            _actionWriter.ValidationResults.Add(validationContext, validated);
                        }
                    }

                    _actionWriter.WriteMatchedMessage(grpcMsg);

                    // register grpcMsg as a variable in _variableManager
                    _variableManager.AddVariable(match.Id ?? Guid.NewGuid().ToString(), grpcMsg);

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
            JsonSerializerOptions options = new() { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());

            foreach (StreamingMessage message in _grpcOutgoingMessages)
            {
                //Console.WriteLine("Outgoing grpc message:");
                //Console.WriteLine($"{JsonSerializer.Serialize(message, options)}");
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
        private void ProcessOutgoingMessages()
        {
            //JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            //options.Converters.Add(new JsonStringEnumConverter());

            foreach (OutgoingMessage message in _actionData.OutgoingMessages)
            {
                // create a StreamingMessage that will be sent to a language worker
                if (string.IsNullOrEmpty(message.ContentCase))
                {
                    throw new NullReferenceException($"The property {nameof(message.ContentCase)} is required to create a {typeof(StreamingMessage)} object");
                }

                StreamingMessage streamingMessage = _grpcMessageProvider.Create(message.ContentCase, message.Content);

                string messageId = message.Id ?? Guid.NewGuid().ToString();

                // resolve "SetVariables" property
                VariableHelper.ResolveVariableMap(message.SetVariables, messageId, streamingMessage);

                // add the variables inside "SetVariables" to VariableManager
                if (message.SetVariables != null)
                {
                    foreach (KeyValuePair<string, string> variable in message.SetVariables)
                    {
                        _variableManager.AddVariable(variable.Key, variable.Value);
                    }
                }

                _variableManager.AddVariable(messageId, streamingMessage);

                _grpcOutgoingMessages.Add(streamingMessage);
            }
        }

        // TODO: debugging methods, to be deleted later
        //private void PrintDictionary(IDictionary<string, object> resolvedVariables)
        //{
        //    JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
        //    options.Converters.Add(new JsonStringEnumConverter());

        //    foreach (KeyValuePair<string, object> pair in resolvedVariables)
        //    {
        //        Console.WriteLine($"Variable: {pair.Key}\nValue: {JsonSerializer.Serialize(pair.Value, options)}");
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
        private void ProcessIncomingMessages()
        {
            //JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            foreach (IncomingMessage message in _actionData.IncomingMessages)
            {
                var messageId = message.Id ?? Guid.NewGuid().ToString();

                // in message.Match, update any default variable '$.' to '$.{messageId}'
                if (message.Match != null)
                {
                    //message.Match.Query = VariableHelper.UpdateSingleDefaultVariableExpression(message.Match.Query ?? string.Empty, messageId);
                    message.Match.Expected = VariableHelper.UpdateSingleDefaultVariableExpression(message.Match.Expected ?? string.Empty, messageId);
                    message.Match.ExpectedExpression = new Expression(message.Match.Expected);

                    _variableManager.Subscribe(message.Match.ExpectedExpression);
                }

                // TODO: to be deleted
                //// in message.Validators, update any default variable '$.' to '$.{messageId}'
                //if (message.Validators != null)
                //{
                //    foreach (var validator in message.Validators)
                //    {
                //        if (validator != null)
                //        {
                //            validator.Query = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Query ?? string.Empty, messageId);
                //            validator.Expected = VariableHelper.UpdateSingleDefaultVariableExpression(validator.Expected ?? string.Empty, messageId);
                //            validator.ExpectedExpression = new Expression(validator.Expected);

                //            _variableManager.Subscribe(validator.ExpectedExpression);
                //        }
                //    }
                //}

                _unmatchedMessages.Add(message);
            }
        }
    }
}
