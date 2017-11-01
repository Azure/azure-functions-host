FROM microsoft/dotnet:2.0.0-runtime-stretch-arm32v7

RUN apt-get update && \
    apt-get install curl tar xz-utils -y && \
    curl -LO https://nodejs.org/dist/v8.9.1/node-v8.9.1-linux-armv7l.tar.xz && \
    ls -al && \
    tar -xvf node-v8.9.1-linux-armv7l.tar.xz && \
    ln -s /node-v8.9.1-linux-armv7l/bin/node /usr/bin/node && \
    ln -s /node-v8.9.1-linux-armv7l/bin/npm /usr/bin/npm && \
    node --version && \
    npm --version

COPY ["azure-functions-runtime", "/azure-functions-runtime"]

ENV AzureWebJobsScriptRoot=/app

EXPOSE 80

CMD ["dotnet", "/azure-functions-runtime/Microsoft.Azure.WebJobs.Script.WebHost.dll"]
