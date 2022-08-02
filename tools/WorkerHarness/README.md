The Worker Harness is a tool that helps validate new features in an out-of-process worker by testing gRPC communication between the host and worker. Language worker developers can leverage this tool to test their out-of-process language worker end-to-end in a simple and efficient manner by eliminating the need to spin up a host process.

# Getting Started
## Configure your environment
- [.NET CLI], which is included with the .NET SDK. To learn how to install the .NET SDK, see [Install .NET Core].
- A worker executable, which is a Functions App executable that uses your language worker extension.

## Install Worker Harness CLI

Worker harness tool is published as a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools), which you can install using the `dotnet tool install` command.

_The tool is currently published to our internal staging feed only. You need to specify the staging feed as the source when installing the tool._

Open a cmd prompt/terminal window and execute the below command.

```cs
dotnet tool install Microsoft.Azure.Functions.Worker.Harness --version <version> --global --add-source <nugetSource>
```
You can find all the available versions of the tool [here](https://azfunc.visualstudio.com/Azure%20Functions/_artifacts/feed/AzureFunctionsTempStaging/NuGet/Microsoft.Azure.Functions.Worker.Harness/versions/1.0.1-Preview2)

Example
```cs
dotnet tool install Microsoft.Azure.Functions.Worker.Harness --version 1.0.1-Preview2 --global --add-source https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsTempStaging/nuget/v3/index.json
```

## Download the queueTrigger.zip
The queueTrigger.zip can be found [here](samples/scenarios).
Unzip the queueTrigger.zip into a folder.
This folder contains:
- Sample scenario files to test a queue trigger. Detailed documentation about a scenario file can be found [here](#scenario).
- A harness.settings.json file, which is required to run `func-harness` command. Detailed documentation about the harness.settings.json file can be found [here](#requires-inputs).

## Prepare the worker executable for the test
- Add a queue-trigger function in your Function App. Skip this step if you already one.
- Build the Function App.

## Update queueload.json
- Go into the queueTrigger folder and open the file "queueload.json"
- Replace `<FunctionScriptFile>` with the `scriptFile` of your function app. 
    - C#: replace `<FunctionScriptFile>` with "TestApp.dll" if your function app has a name "TestApp". 
    - Java: replace `<FunctionScriptFile>` with `path\to\function-folder\\target\\azure-functions\\artifactID\\appname-version.jar`
    - Python: replace `<FunctionScriptFile>` with `functionname\__init__.py`
    - Nodejs: replace `<FunctionScriptFile>` with `path\to\function-folder\functioname\index.js`
- Replace `<QueueTriggerFunctionName>` with the name of the function. For instance, if a queue-trigger function has a name "QueueTrigger", then replace `<FunctionScriptFile>` with "QueueTrigger".
- Replace `<QueueTriggerFunctionEntryPoint>` with the entry point of your function.
    - C#: replace it with `TestApp.QueueTrigger.Run` if TestApp is the function app's name, and QueueTrigger is the function's name.
    - Java: replace it with `com.function.QueueFunction.run` if com.function is the group ID, and QueueTrigger is the function's name.
    - Python: replace it with `main`
    - Nodejs: replace it with an empty string.

![functionload image]

## Update harness.settings.json
- In the queueTrigger folder, open the file "harness.settings.json".
- Replace `<languageExecutable>` with the absolute path of your language executable.
- Replace `<workerExecutable>` with the absolute path of your worker.
- Replace `<workerDirectory>` with the absolute path of your function app directory.
- If you have any executable arguments or worker arguments, put them in the "languageExecutableArguments" list and the "workerArguments" list. They will be added as arguments to the worker process. The difference between the 2 lists is the order: the former is put before the worker path, the later is put after.

![harness.settings.json image]

See some sample "harness.settings.json" [here](samples/harness.settings.json/).

## Run the Worker Harness CLI
- Open a CLI application such as Terminal, Command Prompt.
- `cd` into the queueTrigger folder that you downloaded.
- `func-harness`.
```cs
cd path\to\queueTrigger\folder
func-harness
```

## Interpret the result
If you test this scenario with a stable language worker, the scenario will pass, and you would see the following messages on the CLI application.

![queueTrigger result image]

The "queueTriggerScenario" tests the following sequence:
1. After the harness spawns a language worker, the worker responds with a StartStream message. In a real host-worker interaction, the StartStream message notifies the host that the worker is ready. As you can see in the above image, the first action ("validate worker's ability to sends a StartStream message") validates this StartStream message.
1. Next, the harness and worker exchange WorkerInitRequest - WorkerInitResponse messages. If this were an actual exchange between host and worker, the host would only proceed if the status of the WorkerInitResponse is Success. Thus, in the second action ("validate worker's ability to handle worker initialization"), the harness sends a WorkerInitRequest and validates that worker responds with a Success WorkerInitResponse.
1. Next, the harness and worker exchange FunctionLoadRequest - FunctionLoadResponse messages. After receiving a FunctionLoadRequest, the worker should respond with a FunctionLoadResponse whose Status is Success. The harness will validate this response from worker in the third action ("validate worker's ability to handle function load").
1. Lastly, the harness will send an InvocationRequest that ask the worker to invoke the queue-trigger function. If the worker handles invocation correctly, it will send an InvocationResponse with a Status of Success. In the fourth action, the harness validates this InvocationResponse from worker.

# User Inputs
## Requires Inputs:
- A scenario file. This file follows **Json** format and contains a list of actions to validate against a language worker. The [Scenario](#scenario) section explains the available actions and how to put them together to create a scenario.
- A language executable. E.g. python.exe, dotnet.exe, node.exe, etc.
- A worker path. This is the worker file of your Functions App.
- A function app directory: This is the directory of your Function App.

Put those requirements in a "harness.settings.json" file. For example, a .NET developer would construct the "harness.settings.json" file as followed:

```
{
  "scenarioFile": "C:\\dev\\testings\\scenario_a.json",
  "languageExecutable": "C:\\Program Files\\dotnet\\dotnet.exe",
  "workerPath": "C:\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0\\FunctionApp1.dll",
  "functionAppDirectory": "C:\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0"
}
```

See sample "harness.settings.json" [here](samples/harness.settings.json/).

## Optional:
- Language executable arguments: a list of arguments that go before the worker path in a process's argument. Empty by default.
- Worker arguments: a list of arguments that go after the worker path in a process's argument. Empty by default.
- DisplayVerboseError: `true`/`false`. If true, the Worker Harness displays verbose error messages. The content of a verbose error message depends on the error type. See [Errors](#errors) for more info. The flag is set to `false` by default.

## How to Run
Open the folder that contains your "harness.settings.json" in Terminal or a CLI application of your choice. Then run `func-harness`.
```cs
cd "path\\to\\harness\\settings\\folder"
func-harness
```
The harness will spin up a language worker process just like a real host instance and then execute a scenario.

# Scenario
A scenario consists of the following:
- a __scenarioName__: optional
- a list of **actions**: required

An **action** is a unit of work to be executed by the Worker Harness. Users specify an **action** object with the following properties:
* **actionType** (required): the type of action.
* **actionName** (optional): the name of the action.

```
{
    "scenarioName": "a demo scenario",
    "actions": [
        {
            "actionType": "rpc",
            "actionName": "send and receive messages via gRPC"
        }
    ]
}
```

Additional properties may be required depending on the type of action. The Worker Harness currently supports the following actions: 
- [**Rpc**](#rpc-action): send and receive messages from worker via gRPC.
- [**Delay**](#delay-action): delay the execution of a scenario for a number of miliseconds.
- [**Import**](#import-action): import and execute actions in another scenario file.

## Rpc Action
An **rpc** action sends messages to and validates messages from a language worker through gRPC. Users indicate the content of the messages to send to worker and specify which messages to receive from worker and how to validate them.

The structure of an **rpc** action:
* **timeout** (optional): the amount of time in **_miliseconds_** to execute an **rpc** action. Default to 5000 ms.
* **messages** (required): a list of messages to send, receive, and validate from the language worker.

```
{
    "actionType": "rpc",
    "actionName": "a demo Rpc action",
    "timeout": 10000,
    "messages": [...]
}
```

### Messages:
Because the Worker Harness communicates with the language worker via gRPC, all messages must have a **messageType** property, which indicates the type of [StreamingMessage][StreamingMessage]. The [StreamingMessage][StreamingMessage] class is defined in the Harness's [proto file][harness proto], which mirrors the [proto file][host proto] in the host.

Messages are characterized by the **direction** property: 
- the **outgoing** direction tells the Harness to *construct + send* a message to worker
- the **incoming** direction tells the Harness to *receive + validate* a message from worker.

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
    }
}
```
Given this **outgoing** message, the Worker Harness will construct a StreamingMessage of type [InvocationRequest][InvocationRequest] whose content is the value of the **payload** property, and then send it to language worker.

The Worker Harness only knows how to create [StreamingMessage] types that the host creates, such as [WorkerInitRequest], [FunctionLoadRequest], etc. It __cannot__ create [StreamingMessage] types that the language worker sends, such as [WorkerInitResponse], [FunctionLoadResponse], etc.

In summary, an **outgoing** rpc message has the following properties:
- **direction** (required): outgoing.
- **messageType** (required): the type of [StreamingMessage][StreamingMessage] to send via gRPC to language worker.
- **payload** (required): the content of [StreamingMessage][StreamingMessage] to send via gRPC to language worker.


#### **Incoming Message**:
```
{
    "messageType": "InvocationResponse",
    "direction": "Incoming",
    "matchingCriteria": [{
        "query": "$.InvocationResponse.InvocationId",
        "expected": "93ff2ed4-02c6-427a-aef2-ae1a0697f57d"
    }],
    "validators": [{
        "type": "string",
        "query": "$.InvocationResponse.Result.Status",
        "expected": "Success"
    }]
}
```
During the execution of an **rpc** action, the Worker Harness will listen to all [StreamingMessage][StreamingMessage] sent by the language worker. For each StreamingMessage, the Harness will look through all the __incoming__ messages and choose one message with the same __messageType__ as the StreamingMessage. For instance, if the received StreamingMessage is of type [InvocationResponse], then the Harness will choose the message whose __messageType__ is "InvocationResponse". If the Harness could not find an __incoming__ message with the same __messageType__, that StreamingMessage object is discarded.

 __MatchingCriteria:__ 

Users can apply additional matching requirements in the **matchingCriteria** property. The __matchingCriteria__ is a list of Json objects with 2 properties:
- __query__: a query starts with `$.` followed by the properties of the message that users want to index into. 
For instance, if a StreamingMessage has the following payload, then the query `$.WorkerInitResponse.Result.Status` will return the string `"Failure"`.
```
    {"WorkerInitResponse": { "Result": { "Status": "Failure" } } }
```

- __expected__: this is the value that the __query__ will be compare to. The expected string can be either a variable or a string literal. For more information on variables, see [Variables and Expressions](#variables-and-expressions).

In the above example of an incoming message, when the Harness receives a StreamingMessage that is an InvocationResponse, it will query the StreamingMessage using the `$.InvocationResponse.InvocationId` path. The result of the __query__ is compared to the __expected__ value. If they are equal, the StreamingMessage is a match and will be validated. Otherwise, it will be discarded. 

The Worker Harness will wait for a __timeout__ duration. If no StreamingMessage matches the __messageType__ and __matchingCriteria__ of an incoming message, the Harness will declare a [Message_Not_Received_Error][Message_Not_Received_Error]. 

 __Validators:__

The Worker Harness validates a matched StreamingMessage against the __validators__ list. Similar to __matchingCriteria__, each validator has a __query__ string and an __expected__ string.  

There are two types of __validator__: _regex_ and _string_. 
- If a _regex_ validator is used, then the Harness will use regular expression matching to validate the __query__ result. The __expected__ string should be a regular expression. See [Regular Expression Language - Quick Reference](https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference). 
- If a _string_ validator is used, the Harness will use string comparison to validate the __query__ result. The __expected__ string should be a string literal. 

In the above example of an incoming message, a string validator tells the Worker Harness to query the matched StreamingMessage with the `$.InvocationResponse.Result.Status` path, then compare the query result to the string `Success`. If the string comparison returns equal, the validation succeeds. Otherwise, the Worker Harness will declare a [Validation_Error][Validation_Error].

In summary, an __incoming__ rpc message has the following properties:
- __direction__ (required): incoming.
- __messageType__ (required): the type of [StreamingMessage][StreamingMessage] to receive from the language worker via gRPC.
- __matchingCriteria__ (optional): a list of additional matching criteria to filter [StreamingMessage][StreamingMessage] from the language worker.
- __validators__ (optional): a list of validators to validate a matched [StreamingMessage][StreamingMessage].

### SetVariables
Sometimes users may want to remember a certain property of the current StreamingMessage so that they can use it in a later __rpc__ action. The __setVariables__ property allows users to declare a variable and set it to be a value of a property inside the StreamingMessage. The Worker Harness stores this variable globally so that any subsequent actions can use its value.

The __setVariables__ property contains one or more pairs of variable name and query:
- variable name: the name of the variable that the Harness stores in memory.
- query: the path to tell which property of the message to set the variable to; follow the same syntax as the __query__ property in __matchingCriteria__ and __validators__.

```
{
    "direction": "Outgoing",
    "messageType": "InvocationRequest",
    "payload": {...},
    "setVariables": {
        "invocationId_1": "$.InvocationRequest.InvocationId"
    }
}
```
Let's look at the above message example. Because this message is `outgoing`, the Harness will construct an `InvocationRequest` StreamingMessage and sends it to the worker. Then the Harness will query the StreamingMessage with the path `$.InvocationRequest.InvocationId`. Let's say the query result is a value "xyz". Internally, the variable `invocationId_1` will be mapped to "xyz".

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

__*Nested expressions are not allowed*__ to avoid complexity.
- `${${object}}`, `${@{object}}`, `@{${object}}` are invalid.

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
    }]
}
```
In the above message example, the __expected__ property inside __matchingCriteria__ is an expression that contains an object variable `${message_1}`. Internally, the Harness will look up the `message_1` variable in memory and query it with the path `$.FunctionLoadRequest.FunctionId`. The __expected__ property is then set to the query result.

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

## Import Action
The __Import__ action enables users to compose different scenarios into a single scenario. The __Import__ action has the following properties:
- __actionType__: import
- __scenarioFile__: the absolute path to a scenario file
```
{
    "actionType": "import",
    "scenarioFile": "absolute\\path\\to\\scenario\\file"
}
```
Internally, the Worker Harness will load the scenario file and execute all actions inside that scenario file.

# Errors

## MessageNotReceivedError
### Cause <br>
- This error occurs when the language worker never emits the message of type __messageType__ and meets the __matchingCriteria__ in an rpc action. <br>
- Please refer to [Rpc Action: Incoming Message](#incoming-message) for more info on the __matchingCriteria__ property.

### How to fix the error<br>
- Consider increase the action's __timeout__ if you expects some delay before the worker emits the mesage.<br>
- Consider turning on the [DisplayVerboseError](#optional-flags) flag. The Worker Harness will shows the expected message that is never received from worker. <br>
- Check your language worker's logic. The error could indicate that your worker has a bug that never fires the expected message.

## ValidationError
### Cause<br>
- This error occurs when the language worker has emitted the expected message that meets the __matchingCriteria__ but fails at least one of the __validators__. <br>
- Please refer to [Rpc Action: Incoming Message](#incoming-message) for more info on the __validators__ property.

### How to fix the error <br>
- Consider turning on the [DisplayVerboseError](#optional-flags) flag. The Worker Harness will shows the [StreamingMessage] that meets the __matchingCriteria__ but fails the __validators__. Inspecting the content of the [StreamingMessage] can help developers discover where and how a bug occurs.

## MessageNotSentError
### Cause <br>
- This error occurs when the Worker Harness fails to send an __outgoing__ message to the language worker before __timeout__ occurs. It usually happens if an __outgoing__ message's payload uses a variable that has not been initialized. <br>
Please refer to [Rpc Action: Outgoing Message](#outgoing-message) for more info.

### How to fix the error<br>
- Double check any variables used in the payload. Make sure those variables are declared and initialized in previous actions.
- Consider increase the action's __timeout__ so that the Worker Harness has enough time to construct a [StreamingMessage] from the __payload__ and sends it to the language worker.

## WorkerNotExitError
### Cause <br>
- This error occurs inside a "terminate" action. The Worker Harness will wait for a grace period in seconds for the worker process to shut down. If the Harness does not see that the worker process has exited, the Harness will show this error.

### How to fix the error <br>
- Check the "gracePeriodInSeconds" property in your scenario file. You may want to increase it if your worker takes longer to shut down. 
- Check if your worker implements graceful shutdown.

[harness proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto

[host proto]: https://github.com/Azure/azure-functions-host/blob/features/harness/src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto

[StreamingMessage]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L21

[WorkerInitRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L103

[FunctionLoadRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L243

[FunctionsMetadataRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L304

[InvocationRequest]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L321

[InvocationResponse]: https://github.com/Azure/azure-functions-host/blob/3358f2b665da51a491dd40d59da287348febe9eb/tools/WorkerHarness/src/WorkerHarness.Core/Protos/FunctionRpc.proto#L375

[Message_Not_Received_Error]: #messagenotreceivederror

[Validation_Error]: #validationerror

[Worker Harness NuGet Latest Release]: https://azfunc.visualstudio.com/Azure%20Functions/_artifacts/feed/AzureFunctionsTempStaging/NuGet/Microsoft.Azure.Functions.Worker.Harness/overview/1.0.1-Preview2

[.NET CLI]: https://docs.microsoft.com/en-us/dotnet/core/tools/

[Install .NET Core]: https://docs.microsoft.com/en-us/dotnet/core/install/windows

[dotnet tool]: https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install

[queueTrigger]: https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness/sample%20scenarios

[functionload image]: assets/functionload.png

[harness.settings.json image]: assets/harnessSettings.png

[queueTrigger result image]: assets/queueTriggerResult.png