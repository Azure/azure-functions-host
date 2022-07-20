This tutorial explains step-by-step on how to create a scenario that validate a language worker has successfully started.

# Determine Validation Requirements
After the host spawns a worker process, the first thing it will do is to listen for a [StartStream] message from the worker. Once receiving the [StartStream] message, the host will send a [WorkerInitRequest] message to the worker. When and only when the host receives a [WorkerInitResponse] message with a "Success" result will it continue to work with this worker.

To construct this scenario, we want to imitate the host by sending a [WorkerInitRequest] message and validate that two things:<br>
1. The worker sends a [StartStream] message after being spawned.
2. The worker sends a [WorkerInitResponse] message with a "Success" result after receiving a [WorkerInitRequest].

# Construct Actions
Because we will be sending messages to worker and validate worker's response messages, [Rpc Action] is the right choice. In the scenario file, we will create 2 rpc actions: one to validate a [StartStream] message, and the other to send a [WorkerInitRequest] message and validate a [WorkerInitResponse] message.

## Action 1: Validate a [StartStream] message

## Action 2: Send a [WorkerInitRequest] message and validate a [WorkerInitResponse] message


[StartStream]: https://github.com/Azure/azure-functions-host/blob/6c80f8b0649f964899c30228241969d73275acb7/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto#L97

[WorkerInitRequest]: https://github.com/Azure/azure-functions-host/blob/6c80f8b0649f964899c30228241969d73275acb7/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto#L103

[WorkerInitResponse]: https://github.com/Azure/azure-functions-host/blob/6c80f8b0649f964899c30228241969d73275acb7/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto#L122

[Rpc Action]: https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#rpc-action