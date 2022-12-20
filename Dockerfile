FROM mcr.microsoft.com/dotnet/sdk:6.0 AS installer-env

ENV PublishWithAspNetCoreTargetManifest false

COPY . /workingdir

RUN cd workingdir && \
    dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj --output /azure-functions-host

# Runtime image
FROM mcr.microsoft.com/azure-functions/python:4-python3.9

RUN apt-get update && \
    apt-get install -y gnupg && \
    curl -sL https://deb.nodesource.com/setup_12.x | bash - && \
    apt-get update && \
    apt-get install -y nodejs dotnet-sdk-6.0 && \
    mkdir -p /home/site/wwwroot

# Install the dependencies for Visual Studio Remote Debugger
RUN apt-get update && apt-get install -y --no-install-recommends unzip procps

# Install Visual Studio Remote Debugger
RUN curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l ~/vsdbg

COPY --from=installer-env ["/azure-functions-host", "/azure-functions-host"]

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    HOME=/home \
    ASPNETCORE_URLS=http://+:80 \
    AZURE_FUNCTIONS_ENVIRONMENT=Development \
    FUNCTIONS_WORKER_RUNTIME=python

EXPOSE 80

CMD dotnet /azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost.dll
