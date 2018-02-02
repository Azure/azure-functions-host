FROM microsoft/dotnet:2.0-sdk-stretch AS installer-env

ENV PublishWithAspNetCoreTargetManifest false
ENV ScriptTag v2.0.11415-alpha

RUN apt-get update && \
    apt-get install git -y && \
    git clone https://github.com/Azure/azure-webjobs-sdk-script --depth 1 && \
    cd azure-webjobs-sdk-script && \
    git fetch --all --tags && \
    git checkout tags/${ScriptTag} && \
    dotnet build WebJobs.Script.sln && \
    dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj --output /azure-functions-runtime

# Runtime image
FROM microsoft/dotnet:2.0-sdk-stretch

COPY --from=installer-env ["/azure-functions-runtime", "/azure-functions-runtime"]

ENV AzureWebJobsScriptRoot=/app

ENV WEBSITE_HOSTNAME=localhost:80

CMD ["dotnet", "/azure-functions-runtime/Microsoft.Azure.WebJobs.Script.WebHost.dll"]
