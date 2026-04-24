# Functions.WorkerProxy (gRPC relay + HTTP proxy) — Native AOT
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /repo

COPY . .
RUN dotnet publish src/Functions.WorkerProxy/Functions.WorkerProxy.csproj \
    -c Release -r linux-x64 -o /app/publish \
    && find /app/publish -maxdepth 1 \( -name '*.pdb' -o -name '*.dbg' \) -delete

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --from=build /app/publish/ ./
EXPOSE 80 28080 50053 50054
ENTRYPOINT ["./Microsoft.Azure.Functions.WorkerProxy"]