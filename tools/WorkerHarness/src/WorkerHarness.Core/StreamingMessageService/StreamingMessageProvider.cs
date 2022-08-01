// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Options;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.StreamingMessageService
{
    public class StreamingMessageProvider : IStreamingMessageProvider
    {
        private readonly HarnessOptions _workerOptions;
        private readonly IPayloadVariableSolver _payloadVariableSolver;
        private readonly JsonSerializerOptions _serializerOptions;

        // Exception messages
        internal static string NullPayloadMessage = "Cannot create a {0} message from a null payload";
        internal static string UnsupportedMessageType = "The Worker Harness is currently not able to create a {0} message";

        public StreamingMessageProvider(IOptions<HarnessOptions> workerOptions, 
            IPayloadVariableSolver payloadVariableSolver)
        {
            _workerOptions = workerOptions.Value;
            _payloadVariableSolver = payloadVariableSolver;

            _serializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
            _serializerOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public bool TryCreate(out StreamingMessage message, string messageType, JsonNode? payload, IVariableObservable variableObservable)
        {
            if (payload == null)
            {
                throw new ArgumentException(string.Format(NullPayloadMessage, messageType));
            }

            bool solved = _payloadVariableSolver.TrySolveVariables(out JsonNode newPayload, payload, variableObservable);

            if (solved)
            {
                message = Create(messageType, newPayload);

                return true;
            }
            else
            {
                message = new StreamingMessage();

                return false;
            }
        }

        public StreamingMessage Create(string messageType, JsonNode? payload)
        {
            if (payload == null)
            {
                throw new ArgumentException(string.Format(NullPayloadMessage, messageType));
            }

            StreamingMessage message = new()
            {
                RequestId = Guid.NewGuid().ToString()
            };

            switch (messageType)
            {
                case "WorkerInitRequest":
                    WorkerInitRequest workerInitRequest = CreateWorkerInitRequest(payload);
                    message.WorkerInitRequest = workerInitRequest;
                    break;
                case "FunctionLoadRequest":
                    FunctionLoadRequest functionLoadRequest = CreateFunctionLoadRequest(payload);
                    message.FunctionLoadRequest = functionLoadRequest;
                    break;
                case "InvocationRequest":
                    InvocationRequest invocationRequest = CreateInvocationRequest(payload);
                    message.InvocationRequest = invocationRequest;
                    break;
                case "FunctionsMetadataRequest":
                    FunctionsMetadataRequest functionsMetadataRequest = CreateFunctionsMetadataRequest(payload);
                    message.FunctionsMetadataRequest = functionsMetadataRequest;
                    break;
                case "FunctionLoadRequestCollection":
                    FunctionLoadRequestCollection functionLoadRequestCollection = CreateFunctionLoadRequestCollection(payload);
                    message.FunctionLoadRequestCollection = functionLoadRequestCollection;
                    break;
                case "WorkerTerminate":
                    WorkerTerminate workerTerminate = CreateWorkerTerminate(payload);
                    message.WorkerTerminate = workerTerminate;
                    break;
                case "FileChangeEventRequest":
                    FileChangeEventRequest fileChangeEventRequest = CreateFileChangeEventRequest(payload);
                    message.FileChangeEventRequest = fileChangeEventRequest;
                    break;
                case "InvocationCancel":
                    InvocationCancel invocationCancel = CreateInvocationCancel(payload);
                    message.InvocationCancel = invocationCancel;
                    break;
                case "FunctionEnvironmentReloadRequest":
                    FunctionEnvironmentReloadRequest environmentReloadRequest = CreateFunctionEnvironmentReloadRequest(payload);
                    message.FunctionEnvironmentReloadRequest = environmentReloadRequest;
                    break;
                case "CloseSharedMemoryResourcesRequest":
                    CloseSharedMemoryResourcesRequest closeMemoryRequest = CreateCloseSharedMemoryResourcesRequest(payload);
                    message.CloseSharedMemoryResourcesRequest = closeMemoryRequest;
                    break;
                default:
                    throw new ArgumentException(string.Format(UnsupportedMessageType, messageType));
            }

            return message;
        }

        private CloseSharedMemoryResourcesRequest CreateCloseSharedMemoryResourcesRequest(JsonNode content)
        {
            CloseSharedMemoryResourcesRequest request = JsonSerializer.Deserialize<CloseSharedMemoryResourcesRequest>(content, _serializerOptions)!;

            if (content["MapNames"] != null && content["MapNames"] is JsonArray mapNames)
            {
                IEnumerator<JsonNode?> enumertor = mapNames.GetEnumerator();
                while (enumertor.MoveNext())
                {
                    JsonNode? node = enumertor.Current;
                    if (node == null)
                    {
                        continue;
                    }
                    else
                    {
                        request.MapNames.Add(node.GetValue<string>());
                    }
                }
            }

            return request;
        }

        private FunctionEnvironmentReloadRequest CreateFunctionEnvironmentReloadRequest(JsonNode content)
        {
            FunctionEnvironmentReloadRequest request = JsonSerializer.Deserialize<FunctionEnvironmentReloadRequest>(content, _serializerOptions)!;

            if (string.IsNullOrEmpty(request.FunctionAppDirectory))
            {
                request.FunctionAppDirectory = _workerOptions.FunctionAppDirectory;
            }

            if (content["EnvironmentVariables"] != null && content["EnvironmentVariables"] is JsonObject environmentVariables)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in environmentVariables)
                {
                    string data = pair.Value?.GetValue<string>() ?? string.Empty;
                    request.EnvironmentVariables.Add(pair.Key, data);
                }
            }

            return request;
        }

        private InvocationCancel CreateInvocationCancel(JsonNode content)
        {
            InvocationCancel invocationCancel = JsonSerializer.Deserialize<InvocationCancel>(content, _serializerOptions)!;

            return invocationCancel;
        }

        private FileChangeEventRequest CreateFileChangeEventRequest(JsonNode content)
        {
            FileChangeEventRequest fileChangeEventRequest = JsonSerializer.Deserialize<FileChangeEventRequest>(content, _serializerOptions)!;

            return fileChangeEventRequest;
        }

        private WorkerTerminate CreateWorkerTerminate(JsonNode content)
        {
            WorkerTerminate workerTerminate = JsonSerializer.Deserialize<WorkerTerminate>(content, _serializerOptions)!;

            return workerTerminate;
        }

        private FunctionLoadRequestCollection CreateFunctionLoadRequestCollection(JsonNode content)
        {
            FunctionLoadRequestCollection functionLoadRequestCollection = JsonSerializer.Deserialize<FunctionLoadRequestCollection>(content, _serializerOptions)!;

            if (content["FunctionLoadRequests"] != null && content["FunctionLoadRequests"] is JsonArray contentInJsonArray)
            {
                IEnumerator<JsonNode> enumerator = contentInJsonArray.GetEnumerator()!;
                while (enumerator.MoveNext())
                {
                    JsonNode? functionLoadRequestPayload = enumerator.Current;
                    FunctionLoadRequest functionLoadRequest = CreateFunctionLoadRequest(functionLoadRequestPayload);
                    functionLoadRequestCollection.FunctionLoadRequests.Add(functionLoadRequest);
                }
            }

            return functionLoadRequestCollection;
        }

        private FunctionsMetadataRequest CreateFunctionsMetadataRequest(JsonNode content)
        {
            FunctionsMetadataRequest? functionsMetadataRequest = JsonSerializer.Deserialize<FunctionsMetadataRequest>(content, _serializerOptions)!;

            if (string.IsNullOrEmpty(functionsMetadataRequest.FunctionAppDirectory))
            {
                functionsMetadataRequest.FunctionAppDirectory = _workerOptions.FunctionAppDirectory;
            }

            return functionsMetadataRequest;
        }

        private InvocationRequest CreateInvocationRequest(JsonNode content)
        {
            InvocationRequest invocationRequest = JsonSerializer.Deserialize<InvocationRequest>(content, _serializerOptions)!;

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

        private FunctionLoadRequest CreateFunctionLoadRequest(JsonNode content)
        {
            FunctionLoadRequest? functionLoadRequest = JsonSerializer.Deserialize<FunctionLoadRequest>(content, _serializerOptions)!;

            // if user does not specify a FunctionId in the scenario file, create a new Guid
            if (string.IsNullOrEmpty(functionLoadRequest.FunctionId))
            {
                functionLoadRequest.FunctionId = Guid.NewGuid().ToString();
            }

            functionLoadRequest.Metadata.FunctionId = functionLoadRequest.FunctionId;

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

        private WorkerInitRequest CreateWorkerInitRequest(JsonNode content)
        {
            WorkerInitRequest workerInitRequest = JsonSerializer.Deserialize<WorkerInitRequest>(content, _serializerOptions)!;

            // if the user does not specify the FunctionAppDirectory, use the WorkerDirectory value in the _workerDescription object
            if (string.IsNullOrEmpty(workerInitRequest.FunctionAppDirectory))
            {
                workerInitRequest.FunctionAppDirectory = _workerOptions.FunctionAppDirectory;
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
                    RpcLog.Types.Level value = logCategory.Value != null ? JsonSerializer.Deserialize<RpcLog.Types.Level>(logCategory.Value, _serializerOptions) : RpcLog.Types.Level.Trace;
                    workerInitRequest.LogCategories.Add(logCategory.Key, value);
                }
            }

            return workerInitRequest;
        }
    }
}
