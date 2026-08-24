# WorkerProxy runtime image for the compute-separation worker pod.
# The build context is the standard linux-x64 Native AOT publish output under
# out/pub/Functions.WorkerProxy/release_linux-x64.

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app

# Native AOT currently produces one required runtime artifact. Revisit if the publish layout changes.
COPY Azure.Functions.WorkerProxy ./

ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
ENTRYPOINT ["./Azure.Functions.WorkerProxy"]
