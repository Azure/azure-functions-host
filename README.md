Azure WebJobs SDK
===

|Branch|Windows|macOS/Linux
|---|:---:|:---:|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/3qmk6ukn942q220j/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-rqm4t/branch/master)|[![Build Status](https://travis-ci.org/Azure/azure-webjobs-sdk.svg?branch=master)](https://travis-ci.org/Azure/azure-webjobs-sdk)
|dev|[![Build status](https://ci.appveyor.com/api/projects/status/3qmk6ukn942q220j/branch/dev?svg=true)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-rqm4t/branch/dev)|[![Build Status](https://travis-ci.org/Azure/azure-webjobs-sdk.svg?branch=dev)](https://travis-ci.org/Azure/azure-webjobs-sdk)|
|v2.x|[![Build status](https://ci.appveyor.com/api/projects/status/3qmk6ukn942q220j/branch/v2.x?svg=true)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-rqm4t/branch/v2.x)|-|

The **Azure WebJobs SDK** is a framework that simplifies the task of writing background processing code that runs in Azure. The Azure WebJobs SDK includes a declarative **binding** and **trigger** system that works with Azure Storage Blobs, Queues and Tables as well as Service Bus. The binding system makes it incredibly easy to write code that reads or writes Azure Storage objects. The trigger system automatically invokes a function in your code whenever any new data is received in a queue or blob.

In addition to the built in triggers/bindings, the WebJobs SDK is **fully extensible**, allowing new types of triggers/bindings to be created and plugged into the framework in a first class way. See [Azure WebJobs SDK Extensions](https://github.com/Azure/azure-webjobs-sdk-extensions) for details. Many useful extensions have already been created and can be used in your applications today. Extensions include a File trigger/binder, a Timer/Cron trigger, a WebHook HTTP trigger, as well as a SendGrid email binding. 

Usually you'll host the WebJobs SDK in **Azure WebJobs**, but you can also run your jobs in a Worker Role. The **Azure WebJobs** feature of **Azure Web Apps** provides an easy way for you to run programs such as services or background tasks
in a Web App. You can upload and run an executable file such as an .exe, .cmd, or .bat file to your Web App. In addition to the benefits listed above, using the Azure WebJobs SDK to write WebJobs also provides an integrated **Dashboard** experience in the Azure management portal, with rich monitoring and diagnostics information for your WebJob runs.

## Documentation

See [the documentation](https://github.com/Azure/azure-webjobs-sdk/wiki)

## License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](https://github.com/Azure/azure-webjobs-sdk/blob/master/LICENSE.txt)

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Questions?

See the [getting help](https://github.com/Azure/azure-webjobs-sdk/wiki#getting-help) section in the wiki.
