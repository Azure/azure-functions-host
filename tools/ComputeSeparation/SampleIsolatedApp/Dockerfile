# Use the repository root as the build context.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /src
COPY NuGet.config ./
COPY tools/ComputeSeparation/SampleIsolatedApp/ ./

# The entrypoint owns startup, and the worker uses its generated metadata provider.
RUN dotnet publish SampleIsolatedApp.csproj --configuration Release --runtime linux-x64 --self-contained false \
    -p:PublishReadyToRun=true --output /publish \
    && rm -f /publish/worker.config.json /publish/functions.metadata

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /home/site/wwwroot
COPY --from=build /publish/ ./

USER $APP_UID
ENTRYPOINT ["dotnet", "SampleIsolatedApp.dll"]
