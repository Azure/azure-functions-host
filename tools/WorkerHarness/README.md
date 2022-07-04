Worker Harness is a tool that validates a scenario against a language worker. Language worker developers can leverage this tool to test their isolated language model end-to-end, eliminating the need to spin up a host process.

# Run Worker Harness
Worker Harness is a Console Application. Clone the [Azure/azure-functions-host](https://github.com/Azure/azure-functions-host/) repos to your local machine and open it in Terminal or Command Prompt. Then use the `cd .\tools\WorkerHarness\` command to open the *WorkerHarness* folder.

## User Inputs
*Worker Harness* requires the following inputs from users
- A scenario file. This file follows **Json** format and contains a list of actions to validate against a language worker. The [Scenario](#scenario) section explains the available actions and how to put them together to create a scenario.
- A language executable. E.g. python.exe, dotnet.exe, node.exe, etc.
- A worker executable. This is the worker executable file of your Functions App.
- A worker directory: This is the folder that contains the worker executable, functions metadata file, and libraries/assemblies of your Functions App.

Put the paths to those requirements in *src\WorkerHarness.Console\harness.settings.json*. For example, a .NET developer would construct the *harness.settings.json* as followed

```
{
  "scenarioFile": "C:\\dev\\testings\\scenario_a.json",
  "languageExecutable": "C:\\Program Files\\dotnet\\dotnet.exe",
  "workerExecutable": "C:\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0\\FunctionApp1.dll",
  "workerDirectory": "C:\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0"
}
```


## How to Run
Make sure you are in the *azure-functions-host\tools\WorkerHarness* directory
* Use the `dotnet build` command
```
dotnet build

cd .\src\WorkerHarness.Console\bin\Debug\net6.0\

.\WorkerHarness.Console.exe 
```
* Use the `dotnet run` command
```
dotnet run --project="src\WorkerHarness.Console"
```

# Scenario
A scenario consists of a list of **actions**. A **scenarioName** is optional.
```
{
    "scenarioName": "a demo scenario",
    "actions": []
}
```
An **action** is a unit of work to be executed by the Worker Harness. Users specify an **action** object with the following properties in the scenario file:
* **actionType** (required): the type of action.
* **actionName** (optional): the name of the action.

Additional properties may be required depending on the type of action. Currently, Worker Harness supports 2 types of actions: **rpc** and **delay**.

## Rpc Action
An **rpc** action sends messages to and validates message from a language worker through gRPC. Users indicate the content of the messages to send to worker and specify which messages to receive from worker and how to validate them.

The structure of an **rpc** action:
* **timeout** (optional): the amount of time in **_miliseconds_** to execute an **rpc** action. Default to 5000 ms.
* **messages** (required): a list of messages to send to and receive + validate from the language worker.

### Messages:
Because Worker Harness communicates with the language worker via gRPC, all messages must have a **messageType** type property, which indicates the type of [StreamingMessage][StreamingMessage] defined in Harness's [proto file][harness proto]. The Harness's [proto file][harness proto] mirrors the [proto file][host proto] in the host.

Messages are characterized by **direction** property: the **outgoing** direction tells the Harness to *construct + send* a message to worker, while the **incoming** direction tells the Harness to *receive + validate* a message from worker.

#### **Outgoing Message**:
```
{
    "direction": "Outgoing",
    "messageType": "InvocationRequest",
    "payload": {
        "FunctionId": "function1",
        "InputData": [{
            "name": "myQueueItem",
            "data": { "string": "Hello, world!" }
        }]
    },
    "id": "message_1"
}
```
Given this **outgoing** message, the Worker Harness will construct a StreamingMessage of type [InvocationRequest][InvocationRequest] whose content is the value of the **payload** property, and then send it to language worker.

The harness will keep this [InvocationRequest][InvocationRequest] StreamingMessage in memory and map it to an identifier. Users have the option to choose an identifier by using the **id** property. If the **id** property is excluded, the harness will use a random GUID value.

It is recommended to have an **id** property if users want to reference a message later. For instance, users may want to validate that the language worker has sent an [InvocationResponse][InvocationResponse] message with a matching 'InvocationId' with the [above message](#outgoing-message). When users construct the matching criteria for the [InvocationResponse][InvocationResponse] message, they can reference the above message by using the 'message_1' **id** as a variable. 

See [Incoming Message](#incoming-message) to learn how to construct matching criteria and validators to validate an **incoming** message. 

See [Variables and Expressions](#variables) to learn how to use variable and variable expressions.

#### **Incoming Message**:
```
{
    "direction": "Incoming",
    "messageType": "InvocationResponse",
    "matchingCriteria": [{
        "query": "$.InvocationResponse.InvocationId",
        "expected": "${message_1}.InvocationRequest.InvocationId"
    }],
    "validators": [{
        "type": "string",
        "query": "$.InvocationResponse.Result.Status",
        "expected": "Success"
    }],
    "id": "message_2"
}
```

### Variables and Expressions:


## Delay Action

[harness proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto

[host proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto

[StreamingMessage]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L21

[InvocationRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L321

[InvocationResponse]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L375