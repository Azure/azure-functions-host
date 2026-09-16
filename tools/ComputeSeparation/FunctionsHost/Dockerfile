# Build the current dedicated Functions Host source on Linux with the product's
# ReadyToRun publish contract. The build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /src
COPY . .

# Same restore/build/publish sequence the product's Linux artifact job uses: the
# framework-dependent output is compiled per assembly with ReadyToRun.
# Restore needs PublishReadyToRun so the crossgen2 packs resolve.
# PublishWorkers is the repository's existing switch for dropping the bundled
# language workers from publish; this image starts its worker out of process.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Functions.Host/Functions.Host.csproj -v m -r linux-x64 -p:PublishReadyToRun=true \
    && dotnet build src/Functions.Host/Functions.Host.csproj -v m -c Release -r linux-x64 --no-restore \
    && dotnet publish src/Functions.Host/Functions.Host.csproj -v m -c Release -r linux-x64 --no-build \
        -p:PublishWorkers=false --output /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --from=build /publish/ ./

# The shared options pipeline still probes the worker directory even when no workers are bundled.
# The script root stays empty; logs and secrets stay on ephemeral storage.
RUN mkdir -p /app/workers /app-content /tmp/functions/logs /tmp/functions/secrets

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    AZURE_FUNCTIONS_ENVIRONMENT=Development \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
    AzureWebJobsSecretStorageType=Files \
    AzureWebJobsScriptRoot=/app-content \
    FUNCTIONS_LOG_PATH=/tmp/functions/logs \
    FUNCTIONS_SECRETS_PATH=/tmp/functions/secrets
EXPOSE 8080
ENTRYPOINT ["./Azure.Functions.Host"]
