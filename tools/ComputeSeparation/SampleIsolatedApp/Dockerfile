# Use the repository root as the build context.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /src
COPY tools/ComputeSeparation/SampleIsolatedApp/ ./
COPY out/extension-payload/packages/ /packages/

# The entrypoint owns startup, and the worker uses its generated metadata provider.
RUN dotnet publish SampleIsolatedApp.csproj --configuration Release --runtime linux-x64 --self-contained false \
    -p:PublishReadyToRun=true --output /publish \
    -p:RestoreSources=https://pkgs.dev.azure.com/azfunc/public/_packaging/upstream-public/nuget/v3/index.json \
    -p:RestoreAdditionalProjectSources=/packages -p:RunAnalyzers=true \
    && rm -f /publish/worker.config.json /publish/functions.metadata

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /home/site/wwwroot
COPY --from=build /publish/ ./

USER $APP_UID
ENTRYPOINT ["dotnet", "SampleIsolatedApp.dll"]
