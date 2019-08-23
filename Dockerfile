FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS installer-env

ENV PublishWithAspNetCoreTargetManifest false

COPY . /workingdir

RUN cd workingdir && \
    dotnet build WebJobs.Script.sln && \
    dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj --output /azure-functions-host

# Runtime image
FROM mcr.microsoft.com/azure-functions/python:2.0

RUN apt-get update && \
    apt-get install -y gnupg && \
    curl -sL https://deb.nodesource.com/setup_8.x | bash - && \
    apt-get update && \
    apt-get install -y nodejs dotnet-sdk-2.2

COPY --from=installer-env ["/azure-functions-host", "/azure-functions-host"]

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    HOME=/home \
    ASPNETCORE_URLS=http://+:80 \
    AZURE_FUNCTIONS_ENVIRONMENT=Development \
    FUNCTIONS_WORKER_RUNTIME=

EXPOSE 80

CMD dotnet /azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost.dll
