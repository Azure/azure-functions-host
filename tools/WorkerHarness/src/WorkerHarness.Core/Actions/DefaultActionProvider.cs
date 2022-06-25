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
        private readonly IValidatorFactory _validatorFactory;

        private readonly IMatch _matchService;

        private readonly IGrpcMessageProvider _rpcMessageProvider;

        private readonly IVariableManager _variableManager;

        private readonly Channel<StreamingMessage> _inboundChannel;

        private readonly Channel<StreamingMessage> _outboundChannel;

        private readonly IActionWriter _actionWriter;

        public string Type => ActionType.Default;

        public DefaultActionProvider(IValidatorFactory validatorFactory, 
            IMatch matchService,
            IGrpcMessageProvider rpcMessageProvider,
            IVariableManager variableManager,
            GrpcServiceChannel channel,
            IActionWriter actionWriter)
        {
            _validatorFactory = validatorFactory;
            _matchService = matchService;
            _rpcMessageProvider = rpcMessageProvider;
            _variableManager = variableManager;
            _inboundChannel = channel.InboundChannel;
            _outboundChannel = channel.OutboundChannel;
            _actionWriter = actionWriter;
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
                                     _matchService,
                                     _rpcMessageProvider, 
                                     actionData,
                                     _variableManager,
                                     _inboundChannel,
                                     _outboundChannel,
                                     _actionWriter);
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
            DefaultActionData? actionData = JsonSerializer.Deserialize<DefaultActionData>(actionNode, serializerOptions)!;

            if (actionData == null)
            {
                throw new InvalidOperationException($"Unable to deserialize a {typeof(JsonNode)} object to a {typeof(DefaultActionData)} object");
            }

            // iterate over 'messages' array and populate actionData.IncomingMessages list and actionData.OutgoingMessages list
            if (actionNode["messages"] == null || actionNode["messages"] is not JsonArray)
            {
                throw new MissingFieldException($"Missing the 'messages' array in an action");
            }

            JsonArray jsonMessages = actionNode["messages"]!.AsArray();
            for (int i = 0; i < jsonMessages.Count; i++)
            {
                JsonNode? jsonMessage = jsonMessages[i];
                ValidateMessage(jsonMessage);

                string messageDirection = jsonMessage!["direction"]!.ToString().ToLower();
                if (messageDirection == "incoming")
                {
                    ValidateIncomingMessage(jsonMessage);
                    actionData.IncomingMessages.Add(JsonSerializer.Deserialize<IncomingMessage>(jsonMessage, serializerOptions)!);
                }
                else if (messageDirection == "outgoing")
                {
                    ValidateOutgoingMessage(jsonMessage);
                    actionData.OutgoingMessages.Add(JsonSerializer.Deserialize<OutgoingMessage>(jsonMessage, serializerOptions)!);
                }
                else
                {
                    throw new InvalidDataException("Invalid value for the direction property");
                }
            }

            return actionData;
        }

        // TODO: write private method to check the required fields for incoming message and outgoing message
        private void ValidateMessage(JsonNode? message)
        {
            if (message == null || message is not JsonObject)
            {
                throw new InvalidDataException($"The 'messages' array must contain non-null json object");
            }

            if (message["contentCase"] == null)
            {
                throw new MissingFieldException($"Missing 'contentCase' property in a message object");
            }

            if (message["direction"] == null)
            {
                throw new MissingFieldException($"Missing 'direction' property in a message object");
            }
        }

        private void ValidateIncomingMessage(JsonNode message)
        {
            if (message["match"] == null || message["match"] is not JsonArray)
            {
                throw new InvalidDataException("Missing the 'match' array in an 'incoming' message object");
            }

            if (message["validators"] == null || message["validators"] is not JsonArray)
            {
                throw new InvalidDataException("Missing the 'validators' array in an 'incoming' message object");
            }
        }

        private void ValidateOutgoingMessage(JsonNode message)
        {
            if (message["content"] == null || message["content"] is not JsonObject)
            {
                throw new InvalidDataException("Missing the 'content' object in an 'outgoing' message object");
            }
        }

        public IAction Create(IDictionary<string, string> context)
        {
            throw new NotImplementedException();
        }
    }
}
