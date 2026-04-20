# Functions.WorkerProxy (gRPC relay + HTTP proxy) — Native AOT
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /repo

COPY . .
RUN dotnet publish src/Functions.WorkerProxy/Functions.WorkerProxy.csproj \
    -c Release -r linux-x64 -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/publish/Microsoft.Azure.Functions.WorkerProxy .
EXPOSE 50051 50052 50053 50054
ENTRYPOINT ["./Microsoft.Azure.Functions.WorkerProxy"]
