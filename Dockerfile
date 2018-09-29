FROM microsoft/dotnet:2.1-sdk AS installer-env

ENV PublishWithAspNetCoreTargetManifest false

COPY . /workingdir

RUN cd workingdir && \
    dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj --output /azure-functions-host

# Runtime image
FROM microsoft/dotnet:2.1-aspnetcore-runtime

RUN apt-get update && \
    apt-get install -y gnupg && \
    curl -sL https://deb.nodesource.com/setup_8.x | bash - && \
    apt-get update && \
    apt-get install -y nodejs

COPY --from=installer-env ["/azure-functions-host", "/azure-functions-host"]
# COPY --from=installer-env ["/workingdir/sample", "/home/site/wwwroot"]

ENV AzureWebJobsScriptRoot=/home/site/wwwroot
ENV HOME=/home
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

CMD dotnet /azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost.dll
