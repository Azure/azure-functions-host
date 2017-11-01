# escape=`

# Installer image
FROM microsoft/windowsservercore:1709 AS installer-env

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

# Retrieve .NET Core SDK
ENV DOTNET_SDK_VERSION 2.0.2
ENV DOTNET_SDK_DOWNLOAD_URL https://dotnetcli.blob.core.windows.net/dotnet/Sdk/$DOTNET_SDK_VERSION/dotnet-sdk-$DOTNET_SDK_VERSION-win-x64.zip
ENV DOTNET_SDK_DOWNLOAD_SHA 4864A36D3BE9D460A17D0EBE9D03B17CE224EC18880BCDBC087889F32DDFC2CF3753A1AB7D0414B1E73E863E0D10F5A8381E80EFFC7F7C0A50600DD82A1F0048

# Trigger the population of the local package cache
ENV NUGET_XMLDOC_MODE skip

RUN Invoke-WebRequest $Env:DOTNET_SDK_DOWNLOAD_URL -OutFile dotnet.zip; `
    if ((Get-FileHash dotnet.zip -Algorithm sha512).Hash -ne $Env:DOTNET_SDK_DOWNLOAD_SHA) { `
        Write-Host 'CHECKSUM VERIFICATION FAILED!'; `
        exit 1; `
    }; `
    `
    Expand-Archive dotnet.zip -DestinationPath $Env:ProgramFiles\dotnet; `
    Remove-Item -Force dotnet.zip; `
    setx /M PATH $($Env:PATH + ';' + $Env:ProgramFiles + '\dotnet')

RUN New-Item -Type Directory warmup;; `
    cd warmup; `
    dotnet new; `
    cd .. ;`
    Remove-Item -Force -Recurse warmup

ENV PublishWithAspNetCoreTargetManifest false
RUN iwr https://github.com/Azure/azure-webjobs-sdk-script/archive/dev.zip -OutFile dev.zip; `
    Expand-Archive dev.zip .; `
    cd azure-webjobs-sdk-script-dev; `
    dotnet restore WebJobs.Script.sln; `
    mkdir src\WebJobs.Script.Grpc\Messages\DotNet; `
    cd src\WebJobs.Script.Grpc; `
    Invoke-Command -ScriptBlock {cmd /c '%UserProfile%\.nuget\packages\grpc.tools\1.4.1\tools\windows_x64\protoc.exe Proto\FunctionRpc.proto --csharp_out Messages\DotNet --grpc_out=Messages\DotNet --plugin=protoc-gen-grpc="%UserProfile%\.nuget\packages\grpc.tools\1.4.1\tools\windows_x64\grpc_csharp_plugin.exe" --proto_path=Proto'}; `
    cd ..\.. ; `
    dotnet build WebJobs.Script.sln; `
    dotnet publish src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj --output C:\runtime

# Runtime image
FROM microsoft/dotnet:2.0.0-runtime-nanoserver-1709

COPY --from=installer-env ["c:\\runtime", "C:\\runtime"]

ENV AzureWebJobsScriptRoot=C:\approot

CMD ["dotnet", "C:\\runtime\\Microsoft.Azure.WebJobs.Script.WebHost.dll"]
