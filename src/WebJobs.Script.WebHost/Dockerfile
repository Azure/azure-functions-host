# WebJobs.Script.WebHost (Functions Runtime)
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj \
    -c Release -o /app/publish \
    -p:SkipWorkerPackages=true -p:PublishWorkers=false \
    && mkdir -p /app/publish/workers

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app

# Workers are excluded at build time via ExcludeWorkers + PublishWorkers=false.
# In compute separation mode all execution happens on remote worker pods.
COPY --from=build /app/publish .

EXPOSE 7071
ENTRYPOINT ["dotnet", "Microsoft.Azure.WebJobs.Script.WebHost.dll"]
