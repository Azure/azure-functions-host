using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Default implemenation of IActionProvider
    /// </summary>
    public class DefaultActionProvider : IActionProvider
    {
        private IValidatorFactory _validatorFactory;

        private IGrpcMessageProvider _rpcMessageProvider;

        private IVariableManager _variableManager;

        private Channel<StreamingMessage> _inboundChannel;

        private Channel<StreamingMessage> _outboundChannel;

        public DefaultActionProvider(IValidatorFactory validatorFactory, 
            IGrpcMessageProvider rpcMessageProvider,
            IVariableManager variableManager,
            Channel<StreamingMessage> inboundChannel,
            Channel<StreamingMessage> outboundChannel)
        {
            _validatorFactory = validatorFactory;
            _rpcMessageProvider = rpcMessageProvider;
            _variableManager = variableManager;
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
        }

        /// <summary>
        /// Create a Default Action
        /// </summary>
        /// <param name="actionNode" cref="JsonNode">contains information to create an action</param>
        /// <returns cref="IAction">a DefaultAction object</returns>
        /// <exception cref=""></exception>
        public IAction Create(JsonNode actionNode)
        {
            // 1. create a DefaultActionData that encapsulate info about an action
            DefaultActionData actionData = CreateDefaultActionData(actionNode);
            // 2. create a DefaultAction object
            return new DefaultAction(_validatorFactory, 
                                     _rpcMessageProvider, 
                                     actionData,
                                     _variableManager,
                                     _inboundChannel,
                                     _outboundChannel);
        }

        /// <summary>
        /// Convert an JsonNode object to a DefaultActionData object
        /// </summary>
        /// <param name="actionNode" cref="JsonNode"></param>
        /// <returns>a DefaultActionData</returns>
        private DefaultActionData CreateDefaultActionData(JsonNode actionNode)
        {
            JsonSerializerOptions serializerOptions = new() { PropertyNameCaseInsensitive = true };
            serializerOptions.Converters.Add(new JsonStringEnumConverter());
            DefaultActionData actionData = JsonSerializer.Deserialize<DefaultActionData>(actionNode, serializerOptions)!;
            if (actionData == null)
            {
                throw new InvalidOperationException($"Unable to deserialize a {typeof(JsonNode)} object to a {typeof(DefaultActionData)} object");
            }

            // iterate over 'messages' property and populate actionData.IncomingMessages list and actionData.OutgoingMessages list
            JsonArray jsonMessages = actionNode["messages"] != null ? actionNode["messages"]!.AsArray() : throw new NullReferenceException("The 'messages' property is missing from the JsonNode object");
            for (int i = 0; i < jsonMessages.Count; i++)
            {
                JsonNode jsonMessage = jsonMessages[i] ?? throw new NullReferenceException($"{typeof(JsonNode)} {nameof(jsonMessage)} has a null value");
                string messageDirection = jsonMessage!["direction"]!.ToString().ToLower();
                if (messageDirection == "incoming")
                {
                    actionData.IncomingMessages.Add(JsonSerializer.Deserialize<IncomingMessage>(jsonMessage, serializerOptions)!);
                }
                else if (messageDirection == "outgoing")
                {
                    actionData.OutgoingMessages.Add(JsonSerializer.Deserialize<OutgoingMessage>(jsonMessage, serializerOptions)!);
                }
                else
                {
                    throw new InvalidDataException("Invalid value for the direction property");
                }
            }

            // If a timeout property is not present in the scenario file, default it to 1 minute
            if (actionNode["timeout"] == null)
            {
                actionData.Timeout = 60000;
            }

            return actionData;
        }
    }
}
