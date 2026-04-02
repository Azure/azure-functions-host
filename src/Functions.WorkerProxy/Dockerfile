# Functions.WorkerProxy (gRPC relay + HTTP proxy)
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/Functions.WorkerProxy/Functions.WorkerProxy.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 50051 50052 50053
ENTRYPOINT ["dotnet", "Microsoft.Azure.Functions.WorkerProxy.dll"]
