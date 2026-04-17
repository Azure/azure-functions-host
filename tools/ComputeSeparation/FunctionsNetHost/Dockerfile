# FunctionsNetHost (.NET Isolated Worker Host)
#
# Runs FunctionsNetHost — the native AOT bootstrap process that hosts
# .NET Isolated worker apps. Customer code is mounted at /home/site/wwwroot
# by the platform before calling /admin/worker/assign.
#
# Build context: repository root
#
# Usage:
#   docker build -f tools/ComputeSeparation/FunctionsNetHost/Dockerfile -t functions-nethost .
#   docker run -v /path/to/app:/home/site/wwwroot \
#              -e FUNCTIONS_GRPC_HOST=<proxy-host> \
#              -e FUNCTIONS_GRPC_PORT=50052 \
#              functions-nethost

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS extract-nethost

WORKDIR /extract
COPY NuGet.config .

# Use dotnet restore with a minimal project to pull the NuGet package.
RUN echo '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Azure.Functions.DotNetIsolatedNativeHost" Version="1.0.14" /></ItemGroup></Project>' > /extract/Extract.csproj \
    && dotnet restore Extract.csproj --configfile NuGet.config

RUN CONTENT_DIR=$(find /root/.nuget/packages/microsoft.azure.functions.dotnetisolatednativehost/1.0.14/contentFiles/any/any/workers/dotnet-isolated -maxdepth 0) \
    && cp "${CONTENT_DIR}/bin/FunctionsNetHost" /extract/ \
    && cp "${CONTENT_DIR}/bin/libnethost.so" /extract/ \
    && cp "${CONTENT_DIR}/worker.config.json" /extract/ \
    && cp -r "${CONTENT_DIR}/bin/prelaunchapps" /extract/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble

WORKDIR /app

COPY --from=extract-nethost /extract/FunctionsNetHost ./
COPY --from=extract-nethost /extract/libnethost.so ./
COPY --from=extract-nethost /extract/worker.config.json ./
COPY --from=extract-nethost /extract/prelaunchapps/ ./prelaunchapps/

# Pre-create the customer code directory (platform mounts content here).
RUN mkdir -p /home/site/wwwroot

ENV HOME=/home
ENV FUNCTIONS_GRPC_HOST=localhost
ENV FUNCTIONS_GRPC_PORT=50052

ENTRYPOINT ["/bin/sh", "-c", \
    "exec /app/FunctionsNetHost FunctionsNetHost --functions-uri http://${FUNCTIONS_GRPC_HOST}:${FUNCTIONS_GRPC_PORT} --functions-request-id dev-request-id --functions-worker-id w_nethost01 --functions-grpc-max-message-length 134217728"]
