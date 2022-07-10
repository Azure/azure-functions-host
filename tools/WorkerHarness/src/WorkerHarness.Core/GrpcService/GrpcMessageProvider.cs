using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Options;

namespace WorkerHarness.Core
{
    public class GrpcMessageProvider : IGrpcMessageProvider
    {
        private readonly HarnessOptions _workerOptions;

        private readonly JsonSerializerOptions _serializerOptions;

        public GrpcMessageProvider(IOptions<HarnessOptions> workerOptions)
        {
            _workerOptions = workerOptions.Value;

            _serializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
            _serializerOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public StreamingMessage Create(string contentCase, JsonNode? content)
        {
            StreamingMessage message = new StreamingMessage
            {
                RequestId = Guid.NewGuid().ToString()
            };

            switch (contentCase)
            {
                case "WorkerInitRequest":
                    WorkerInitRequest workerInitRequest = CreateWorkerInitRequest(content);
                    message.WorkerInitRequest = workerInitRequest;
                    break;
                case "FunctionLoadRequest":
                    FunctionLoadRequest functionLoadRequest = CreateFunctionLoadRequest(content);
                    message.FunctionLoadRequest = functionLoadRequest;
                    break;
                case "InvocationRequest":
                    InvocationRequest invocationRequest = CreateInvocationRequest(content);
                    message.InvocationRequest = invocationRequest;
                    break;
                default:
                    throw new ArgumentException($"Worker Harness does not understand {contentCase} Grpc message");
            }

            return message;
        }

        private InvocationRequest CreateInvocationRequest(JsonNode? content)
        {
            if (content == null)
            {
                throw new ArgumentNullException($"Can't create a {typeof(InvocationRequest)} from a null {typeof(JsonNode)}");
            }

            InvocationRequest? invocationRequest = JsonSerializer.Deserialize<InvocationRequest>(content, _serializerOptions);
            if (invocationRequest == null)
            {
                throw new NullReferenceException($"Cannot deserialize a {typeof(JsonNode)} object to a {typeof(InvocationRequest)} object");
            }

            // if user does not specify a FunctionId in the scenario file, create a new Guid
            if (string.IsNullOrEmpty(invocationRequest.FunctionId))
            {
                invocationRequest.FunctionId = Guid.NewGuid().ToString();
            }

            // if user does nto specify an InvocationId in the scenario file, create a new Guid
            if (string.IsNullOrEmpty(invocationRequest.InvocationId))
            {
                invocationRequest.InvocationId = Guid.NewGuid().ToString();
            }

            // Since the InputData, TriggerMetadata, and TraceContext.Attributes do not expose a public set methods,
            // we can't deserialize them. We have to manually iterate through the user input (if any)
            if (content["InputData"] != null && content["InputData"] is JsonArray inputDataInJsonArray)
            {
                for (int i = 0; i < inputDataInJsonArray.Count; i++)
                {
                    ParameterBinding? parameterBinding = JsonSerializer.Deserialize<ParameterBinding>(inputDataInJsonArray[i], _serializerOptions);
                    invocationRequest.InputData.Add(parameterBinding);
                }
            }

            if (content["TriggerMetadata"] != null && content["TriggerMetadata"] is JsonObject triggerMetadataInJsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in triggerMetadataInJsonObject)
                {
                    TypedData? data = JsonSerializer.Deserialize<TypedData>(pair.Value, _serializerOptions);
                    invocationRequest.TriggerMetadata.Add(pair.Key, data);
                }
            }

            if (content["TraceContext"] != null && content["TraceContext"]!["Attributes"] != null && content["TraceContext"]!["Attributes"] is JsonObject attributesInJsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in attributesInJsonObject)
                {
                    string value = pair.Value != null ? pair.Value.ToString() : string.Empty;
                    invocationRequest.TraceContext.Attributes.Add(pair.Key, value);
                }
            }

            return invocationRequest;
        }

        private FunctionLoadRequest CreateFunctionLoadRequest(JsonNode? content)
        {
            if (content == null)
            {
                throw new ArgumentNullException($"Can't create a {typeof(FunctionLoadRequest)} from a null {typeof(JsonNode)}");
            }

            FunctionLoadRequest? functionLoadRequest = JsonSerializer.Deserialize<FunctionLoadRequest>(content, _serializerOptions);
            if (functionLoadRequest == null)
            {
                throw new NullReferenceException($"Cannot deserialize a {typeof(JsonNode)} object to a {typeof(FunctionLoadRequest)} object");
            }

            // if user does not specify a FunctionId in the scenario file, create a new Guid
            if (string.IsNullOrEmpty(functionLoadRequest.FunctionId))
            {
                functionLoadRequest.FunctionId = Guid.NewGuid().ToString();
            }

            // Since FunctionLoadRequest does not expose a public set methods for Bindings and RawBindings properties,
            // these properties can't be deserialize. We have to manually iterate through user input and set them.
            if (content["Metadata"] != null && content["Metadata"]!["Bindings"] != null && content["Metadata"]!["Bindings"] is JsonObject bindingsInJsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in bindingsInJsonObject)
                {
                    BindingInfo? bindingValue = JsonSerializer.Deserialize<BindingInfo>(pair.Value, _serializerOptions);
                    functionLoadRequest.Metadata.Bindings.Add(pair.Key, bindingValue);
                }
            }

            if (content["Metadata"] != null && content["Metadata"]!["RawBindings"] != null && content["Metadata"]!["RawBindings"] is JsonArray rawBindingsInJsonArray)
            {
                for (int i = 0; i < rawBindingsInJsonArray.Count; i++)
                {
                    string value = rawBindingsInJsonArray[i] != null ? rawBindingsInJsonArray[i]!.ToString() : string.Empty;
                    functionLoadRequest.Metadata.RawBindings.Add(value);
                }
            }

            return functionLoadRequest;
        }

        private WorkerInitRequest CreateWorkerInitRequest(JsonNode? content)
        {
            if (content == null)
            {
                throw new ArgumentNullException($"Can't create a {typeof(WorkerInitRequest)} from a null {typeof(JsonNode)}");
            }

            WorkerInitRequest? workerInitRequest = JsonSerializer.Deserialize<WorkerInitRequest>(content, _serializerOptions);
            if (workerInitRequest == null)
            {
                throw new NullReferenceException($"Cannot deserialize a {typeof(JsonNode)} object to a {typeof(WorkerInitRequest)} object");
            }

            // if the user does not specify the FunctionAppDirectory, use the WorkerDirectory value in the _workerDescription object
            if (string.IsNullOrEmpty(workerInitRequest.FunctionAppDirectory))
            {
                workerInitRequest.FunctionAppDirectory = _workerOptions.WorkerDirectory;
            }

            // if the user does not specify the WorkerDirectory, use the WorkerDirectory value in the _workerDescription object
            if (string.IsNullOrEmpty(workerInitRequest.WorkerDirectory))
            {
                workerInitRequest.WorkerDirectory = _workerOptions.WorkerDirectory;
            }

            // if the user does not specify the HostVersion, use the HostVersion constant
            if (string.IsNullOrEmpty(workerInitRequest.HostVersion))
            {
                workerInitRequest.HostVersion = HostConstants.HostVersion;
            }

            // since WorkerInitRequest does not expose a public set method for the 'Capabilities' and 'LogCategories' property,
            // these properties won't be deserialized. We have to manually iterate through the user input (if any)
            if (content["Capabilities"] != null && content["Capabilities"] is JsonObject capabilitiesInJsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> capability in capabilitiesInJsonObject)
                {
                    string value = capability.Value != null ? capability.Value.GetValue<string>() : string.Empty;
                    workerInitRequest.Capabilities.Add(capability.Key, value);
                }
            }

            if (content["LogCategories"] != null && content["LogCategories"] is JsonObject logCategoriesInJsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> logCategory in logCategoriesInJsonObject)
                {
                    RpcLog.Types.Level value = logCategory.Value != null ? JsonSerializer.Deserialize<RpcLog.Types.Level>(logCategory.Value, _serializerOptions) : RpcLog.Types.Level.Information;
                    workerInitRequest.LogCategories.Add(logCategory.Key, value);
                }
            }

            return workerInitRequest;
        }
    }
}
