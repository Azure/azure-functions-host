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
The Worker Harness will spin up a language worker process just like a real Functions host instance and then execute a scenario.

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

Additional properties may be required depending on the type of action. Currently, Worker Harness supports 2 types of actions: [**Rpc**](#rpc-action) and [**Delay**](#delay-action).

## Rpc Action
An **rpc** action sends messages to and validates message from a language worker through gRPC. Users indicate the content of the messages to send to worker and specify which messages to receive from worker and how to validate them.

The structure of an **rpc** action:
* **timeout** (optional): the amount of time in **_miliseconds_** to execute an **rpc** action. Default to 5000 ms.
* **messages** (required): a list of messages to send to and receive + validate from the language worker.

```
{
    "actionType": "rpc",
    "actionName": "a demo Rpc action",
    "timeout": 10000,
    "messages": [...]
}
```

### Messages:
Because Worker Harness communicates with the language worker via gRPC, all messages must have a **messageType** property, which indicates the type of [StreamingMessage][StreamingMessage]. The [StreamingMessage][StreamingMessage] class is defined in the Harness's [proto file][harness proto], which mirrors the [proto file][host proto] in the host.

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

The harness will keep this InvocationRequest message in memory and map it to an identifier. Users have the option to choose an identifier by using the **id** property. If the **id** property is excluded, the harness will use a random GUID value.

It is recommended to have an **id** property if users want to reference a message later. For instance, users may want to validate that the language worker has sent an [InvocationResponse][InvocationResponse] message with the same 'InvocationId' with the above InvocationRequest message. When constructing the matching criteria for an InvocationResponse message, users reference the above message by using the 'message_1' **id** as a variable. 

In summary, an **outgoing** rpc message has the following properties:
- **direction** (required): outgoing.
- **messageType** (required): the type of [StreamingMessage][StreamingMessage] to send via gRPC to language worker.
- **payload** (required): the content of [StreamingMessage][StreamingMessage] to send via gRPC to language worker.
- **id** (optional): a user-assigned identifier of the [StreamingMessage][StreamingMessage].

See [Incoming Message](#incoming-message) to learn how to construct matching criteria and validators to validate an **incoming** message. 

See [Variables and Expressions](#variables) to learn how to use variable and variable expressions.

#### **Incoming Message**:
```
{
    "direction": "Incoming",
    "messageType": "WorkerInitResponse",
    "matchingCriteria": [{
        "query": "$.ContentCase",
        "expected": "WorkerInitResponse"
    }],
    "validators": [{
        "type": "string",
        "query": "$.WorkerInitResponse.Result.Status",
        "expected": "Success"
    }],
    "id": "message_2",
}
```
During the execution of an **rpc** action, the Worker Harness will listen to all incoming [StreamingMessage][StreamingMessage] sent by the language worker. By default, the Harness will filter incoming messages based on the **messageType** property. If the **matchingCriteria** and **validators** properties are excluded, the Worker Harness will declare success if it receives a message of type **messageType** from the language worker.

 __MatchingCriteria:__ 

Users can apply stricter filters by using the **matchingCriteria** property. Each criterion inside the **matchingCriteria** list contains a **query** string and an **expected** string. The **query** string is a variable expression (see [Variables and Expressions](#variables-and-expressions)) that tells the Harness which property of the incoming message to match. The **expected** string is the value that the **query** will compare to. The **expected** string can be a variable expression or a string literal. 

In the above incoming - message example, the `"$.ContentCase"` **query** tells the Harness to query any incoming 'WorkerInitRequest' message for a 'ContentCase' property. It then compares the string value of the 'ContentCase' property to the string literal `"WorkerInitResponse"` in the __expected__ property. If they are equal, the Harness has received a matched message from the language worker and will validate the matched message if there are any __validators__. If they are not equal, the Harness will discard this 'WorkerInitRequest' message and continue to wait for a matched message. If no matched message is received within the **timeout** period, the Harness will declare a [Message_Not_Received_Error][Message_Not_Received_Error].

 __Validators:__

Users validate a matched message by using the __validators__ property. Similar to __matchingCriteria__, each validator has a __query__ string and an __expected__ string. The **query** string is a variable expression (see [Variables and Expressions](#variables-and-expressions)) that tells the Harness which property of the matched message to validate. The **expected** string is the value that the **query** will compare to. The **expected** string can be a variable expression or a string literal. 

There are two types of __validator__: _regex_ and _string_. 
- If a _regex_ validator is used, then the Harness will use regular expression matching to validate the __query__ string. The __expected__ string should be a regular expression. See [Regular Expression Language - Quick Reference](https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference). 
- If a _string_ validator is used, the Harness will use string comparison to validate the __query__ string. The __expected__ string should be a string literal. A validator is default to be a _string_ validator if the **type** property is omitted.

In the above incoming - message example, a string validator tells the Worker Harness to query into the 'Status' property of a matched 'WorkerInitResponse' message and compare it to the `"Success"` string literal. If the string comparison returns equal, the validation succeeds. Otherwise, the Worker Harness will declare a [Validation_Error][Validation_Error].

> Since both regular expression and string comparison work on string literal, the Worker Harness requires the variable expressions in the __query__ and __expected__ property to evaluate to string values. If they evaluate to objects, the Harness will throw an exception. This requirement applies to both __matchingCriteria__ and __validators__.

In summary, an __incoming__ rpc message has the following properties:
- __direction__ (required): incoming.
- __messageType__ (required): the type of [StreamingMessage][StreamingMessage] to receive from the language worker via gRPC.
- __matchingCriteria__ (optional): a list of additional matching criteria to filter [StreamingMessage][StreamingMessage] from the language worker.
- __validators__ (optional): a list of validators to validate a matched [StreamingMessage][StreamingMessage].
- __id__ (optional): a user-assigned identifier of the matched [StreamingMessage][StreamingMessage].

### SetVariables
An __Rpc__ action remembers the [StreamingMessage][StreamingMessage] that it sends to and receives from the language worker. Users can assign these messages a variable name by setting the __id__ property. Later, they can use these messages as object variable in an expression (see [Variables and Expressions](#variables-and-expressions)). 

Additionally, users can use the __setVariables__ property to declare a variable and initialize it to be the value of any property within a message.
```
{
    "direction": "Outgoing",
    "messageType": "InvocationRequest",
    ...
    "setVariables": {
        "invocationId_1": "$.InvocationRequest.InvocationId"
    }
}
```
In this example, an `invocationId_1` variable is set to be the value of the 'InvocationId' of the 'InvocationRequest' message. The __Rpc__ action that execute this message will store this variable in memory until timeout. 

### Variables and Expressions:
The Worker Harness supports two types of variables: 
- object variable: `${...}`
- string variable: `@{...}`

Each variable expression contains __*one and only one*__ object variable. The object variable must be __*at the beginning*__ of each expression. 
- `${message_1}.InvocationRequest.InvocationId` is valid.
- `${A}.${B}.C` is invalid because it contains 2 object variabless
- `A.${B}.C` is invalid because the object variable is not at the start of the expression.

There is no limit to the number of string expression.
- `${obj_var}.@{str_var_1}.@{str_var_2}` is valid.

Nested expressions are not allowed to avoid complexity.
- `${${object}}`, `${@{object}}`, `@{${object}}` are invalid.

The Worker Harness supports the default object variable `$.`. If an expression uses `$.`, the enclosing message is the implicit object variable. 

```
{
    "direction": "Incoming",
    "messageType": "FunctionLoadResponse",
    "matchingCriteria": [{
        "query": "$.FunctionLoadResponse.FunctionId",
        "expected": "${message_1}.FunctionLoadRequest.FunctionId"
    }],
    "validators": [{
        "type": "string",
        "query": "$.FunctionLoadResponse.Result.Status",
        "expected": "Success"
    }],
    "id": "message_2",
}
```
The default object variable is used in both __queries__ above. The Worker Harness, after receiving a 'FunctionLoadResponse' message from the language worker, will use the message as an object variable for both __queries__.
- `$.FunctionLoadResponse.FunctionId`: the Harness will recursively index into the 'FunctionLoadResponse' property, then 'FunctionId' property of the message.
- `$.FunctionLoadResponse.Result.Status`: the Harness will recursively index into the 'FunctionLoadResponse' property, then 'Result' property, then finally the 'Status' property of the message.

The `${message_1}.FunctionLoadRequest.FunctionId` expression contains an object variable `${message_1}`. The Harness will look up the `message_1` variable in memory and recursively index into it to find the `FunctionId` property.

### Order of Messages in an Rpc Action
An __Rpc__ action sends to and receives messages from the language worker asynchronously, which means that the order of messages in the __messages__ property is not guaranteed. 

In a real host - worker interaction, the worker fires a message in response to a request from the host. In order to emulate this order or dependency between 2 messages, users can leverage the __id__ property, __setVariables__ property, and variable - expression capability when creating a scenario.

For instance, a language developer wants to create a scenario where the host sends an [InvocationRequest][InvocationRequest] message to a worker, and validates that the worker reply with an [InvocationResponse][InvocationResponse] message. An __Rpc__ action for this scenario would look like the following.
```
{
    "actionType": "rpc",
    "actionName": "send an invocation request and validate the invocation response",
    "messages": [
        {
            "direction": "outgoing",
            "messageType": "InvocationRequest",
            "content": {...},
            "setVariables": [{
                "invocationId_1": "$.InvocationRequest.InvocationId"
            }]
        },
        {
            "direction": "incoming",
            "messageType": "InvocationResponse",
            "matchingCriteria": [{
                "query": "$.InvocationResponse.InvocationId",
                "expected": "@{invocationId_1}"
            }],
            "validators": [{
                "query": "$.InvocationResponse.Result.Status",
                "expected": "Success"
            }]
        }
    ]
}
```
This __Rpc__ action validates that an 'InvocationResponse' message with the same 'InvocationId' has been fired by the language worker in response to an "InvocationResponse" message.

## Delay Action
The __Delay__ action delays the exection of a scenario for a certain period of time. A __Delay__ action includes the following properties:
- __actionType__: delay
- __delay__: the amount of time in __*miliseconds*__ to delay.
```
{
    "actionType": "delay",
    "delay": 2000
}
```


[harness proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto

[host proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto

[StreamingMessage]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L21

[InvocationRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L321

[InvocationResponse]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L375

<!--- TODO: Replace with real links --->
[Message_Not_Received_Error]: https://github.com/azure/azure-functions-host

<!--- TODO: Replace with real links --->
[Validation_Error]: https://github.com/azure/azure-functions-host