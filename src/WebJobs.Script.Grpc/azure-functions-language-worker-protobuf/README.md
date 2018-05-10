# Azure Functions Languge Worker Protobuf

This repository contains the protobuf definition file which defines the gRPC service which is used between the Azure WebJobs Script host and the Azure Functions language workers. This repo is shared across many repos in many languages (for each worker) by using git commands.

To use this repo in Azure Functions language workers, follow steps below to add this repo as a subtree (*Adding This Repo*). If this repo is already embedded in a language worker repo, follow the steps to update the consumed file (*Pulling Updates*).

Learn more about Azure Function's projects on the [meta](https://github.com/azure/azure-functions) repo.

## Adding This Repo

From within the Azure Functions language worker repo:
1.	Define remote branch for cleaner git commands
    -	`git remote add proto-file https://github.com/azure/azure-functions-language-worker-protobuf.git`
    -	`git fetch proto-file`
2.	Index contents of azure-functions-worker-protobuf to language worker repo
    -	`git read-tree  --prefix=<path in language worker repo> -u proto-file/<version branch>`
3.	Add new path in language worker repo to .gitignore file
    -   In .gitignore, add path in language worker repo
4.	Finalize with commit
    -	`git commit -m "Added subtree from https://github.com/azure/azure-functions-language-worker-protobuf. Branch: <version branch>. Commit: <latest protobuf commit hash>"`
    -	`git push`

## Pulling Updates

From within the Azure Functions language worker repo:
1.	Define remote branch for cleaner git commands
    -	`git remote add proto-file https://github.com/mhoeger/azure-functions-language-worker-protobuf.git`
    -	`git fetch proto-file`
2.	Merge updates
    -   `git merge -s subtree proto-file/<version branch> --squash --allow-unrelated-histories` 
        -   You can also merge with an explicit path to subtree: `git merge -X subtree=<path in language worker repo> --squash proto-file/<version branch> --allow-unrelated-histories`
3.	Finalize with commit
    -	`git commit -m "Updated subtree from https://github.com/azure/azure-functions-language-worker-protobuf. Branch: <version branch>. Commit: <latest protobuf commit hash>"`
    -	`git push`
	
## Consuming FunctionRPC.proto
*Note: Update versionNumber before running following commands*

## CSharp
```
set NUGET_PATH=%UserProfile%\.nuget\packages
set GRPC_TOOLS_PATH=%NUGET_PATH%\grpc.tools\<versionNumber>\tools\windows_x86
set PROTO_PATH=.\azure-functions-language-worker-protobuf\src\proto
set PROTO=.\azure-functions-language-worker-protobuf\src\proto\FunctionRpc.proto
set PROTOBUF_TOOLS=%NUGET_PATH%\google.protobuf.tools\<versionNumber>\tools
set MSGDIR=.\Messages

if exist %MSGDIR% rmdir /s /q %MSGDIR%
mkdir %MSGDIR%

set OUTDIR=%MSGDIR%\DotNet
mkdir %OUTDIR%
%GRPC_TOOLS_PATH%\protoc.exe %PROTO% --csharp_out %OUTDIR% --grpc_out=%OUTDIR% --plugin=protoc-gen-grpc=%GRPC_TOOLS_PATH%\grpc_csharp_plugin.exe --proto_path=%PROTO_PATH% --proto_path=%PROTOBUF_TOOLS% 
```
## Java
--TODO--

## JavaScript
Maven plugin : [protobuf-maven-plugin](https://www.xolstice.org/protobuf-maven-plugin/)
In pom.xml add following under configuration for this plugin
<protoSourceRoot>${basedir}/<path to this repo>/azure-functions-language-worker-protobuf/src/proto</protoSourceRoot>

## Python
--TODO

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
