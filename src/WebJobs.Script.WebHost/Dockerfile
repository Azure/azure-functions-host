# WebJobs.Script.WebHost (Functions Runtime)
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 7071
ENTRYPOINT ["dotnet", "Microsoft.Azure.WebJobs.Script.WebHost.dll"]
