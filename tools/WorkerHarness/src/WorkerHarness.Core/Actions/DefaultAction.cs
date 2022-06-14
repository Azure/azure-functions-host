using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class DefaultAction : IAction
    {
        private IValidatorManager _validatorManager;
        private IGrpcMessageProvider _grpcMessageProvider;
        private DefaultActionData _actionData;
        private IDictionary<string, object> _globalVariables;

        internal DefaultAction(IValidatorManager validatorManager, IGrpcMessageProvider grpcMessageProvider, DefaultActionData actionData)
        {
            _validatorManager = validatorManager;
            _grpcMessageProvider = grpcMessageProvider;
            _actionData = actionData;
            _globalVariables = new Dictionary<string, object>();
        }

        public string? Type { get => _actionData.Type; }

        public string? Name { get => _actionData.Name; }

        public int? Timeout { get => _actionData.Timeout; }

        public void Execute()
        {
            ProcessOutgoingMessages();
            ProcessIncomingMessages();
        }

        private IEnumerable<StreamingMessage> ProcessOutgoingMessages()
        {
            IList<StreamingMessage> grpcOutgoingMessages = new List<StreamingMessage>();

            JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());

            foreach (OutgoingMessage message in _actionData.OutgoingMessages)
            {
                // create a StreamingMessage that will be sent to a language worker
                if (string.IsNullOrEmpty(message.ContentCase))
                {
                    throw new NullReferenceException($"The property {nameof(message.ContentCase)} is required to create a {typeof(StreamingMessage)} object");
                }

                StreamingMessage streamingMessage = _grpcMessageProvider.Create(message.ContentCase, message.Content);

                // buffer streamingMessage in the _globalVariables dictionary
                string messageId = message.Id ?? Guid.NewGuid().ToString();
                _globalVariables[messageId] = streamingMessage;

                // update _globalVariables with the "SetProperties"
                if (message.SetVariables != null)
                {
                    VariableHelper.UpdateDefaultVariableExpressions(message.SetVariables, messageId);
                    IDictionary<string, object> resolvedVariables = VariableHelper.ResolveVariableExpressions(message.SetVariables, _globalVariables);
                    VariableHelper.UpdateMap(_globalVariables, resolvedVariables);
                }

                grpcOutgoingMessages.Add(streamingMessage);
            }

            return grpcOutgoingMessages;
        }

        private void PrintDictionary(IDictionary<string, object> resolvedVariables)
        {
            JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());

            foreach (KeyValuePair<string, object> pair in resolvedVariables)
            {
                Console.WriteLine($"Variable: {pair.Key}\nValue: {JsonSerializer.Serialize(pair.Value, options)}");
            }
        }

        private void ProcessIncomingMessages()
        {
            JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };
            foreach (IncomingMessage message in _actionData.IncomingMessages)
            {
                var objectName = message.Id ?? Guid.NewGuid().ToString();
                IDictionary<string, string> placeholder = new Dictionary<string, string>();

                // in message.Match, update any default variable '$.' to '$.{message.Id}'
                if (message.Match != null)
                {
                    placeholder["matchQuery"] = message.Match.Query ?? String.Empty;
                    placeholder["matchExpected"] = message.Match.Expected ?? String.Empty;

                    VariableHelper.UpdateDefaultVariableExpressions(placeholder, objectName);

                    message.Match.Query = placeholder["matchQuery"];
                    message.Match.Expected = placeholder["matchExpected"];
                    message.Match.ExpectedExpression = new Expression(message.Match.Expected);
                }

                placeholder.Clear();

                // in message.Validators, update any default variable '$.' to '$.{message.Id}'
                if (message.Validators != null)
                {
                    foreach (var validator in message.Validators)
                    {
                        if (validator != null)
                        {
                            placeholder["validatorQuery"] = validator.Query ?? String.Empty;
                            placeholder["validatorExpected"] = validator.Expected ?? String.Empty;

                            VariableHelper.UpdateDefaultVariableExpressions(placeholder, objectName);

                            validator.Query = placeholder["validatorQuery"];
                            validator.Expected = placeholder["validatorExpected"];
                            validator.ExpectedExpression = new Expression(validator.Expected);
                        }
                    }
                }
                Console.WriteLine("Incoming messages: ");
                Console.WriteLine(JsonSerializer.Serialize(message, options));
                Console.WriteLine("*******************************************************");
                Task.Delay(1000).Wait();
            }
        }
    }
}
