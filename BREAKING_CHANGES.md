# Breaking Changes
These changes list where the experience differs between Functions versions.

## Functions 2.x (changes from 1.x)
### Contract Changes
#### Function.json
Topic         | V1      | V2 
---         | ---       | --- 
Disabling functions | Set `disabled: false` in function.json  | Set environment/App Settings variable `<FunctionName>.Disabled = true` 


### Behavior changes 
Topic         | V1      | V2 
---         | ---       | --- 
Using triggers and bindings (except HTTP, Timer, and Azure Storage) | N/A  | See [documentation](https://docs.microsoft.com/en-us/azure/azure-functions/functions-triggers-bindings#register-binding-extensions) on registering binding extensions

### External Dependency Changes
Some changes are due to changes in our dependencies.
Topic         | V1      | V2 
---         | ---       | --- 
ServiceBus SDK binding object in C# | [`BrokeredMessage`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.servicebus.messaging.brokeredmessage?view=azure-dotnet) class  | [`Message`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.servicebus.message?view=azure-dotnet) class
Twilio SDK binding object in C# | `SMSMessage` class | [`CreateMessageOptions`](https://www.twilio.com/docs/libraries/reference/twilio-php/5.7.3/class-Twilio.Rest.Api.V2010.Account.CreateMessageOptions.html) class