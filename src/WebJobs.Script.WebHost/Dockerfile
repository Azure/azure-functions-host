# WebJobs.Script.WebHost (Functions Runtime)
# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# In compute separation mode the runtime has no local workers — all function
# execution happens on remote worker pods. Remove bundled worker binaries to
# shrink the image. The empty directory must remain because the host probes it
# at startup.
RUN rm -rf /app/workers/* && mkdir -p /app/workers

EXPOSE 7071
ENTRYPOINT ["dotnet", "Microsoft.Azure.WebJobs.Script.WebHost.dll"]
