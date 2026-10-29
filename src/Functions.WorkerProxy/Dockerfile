# WorkerProxy runtime image for the compute-separation worker pod.
# The build context is the standard linux-x64 Native AOT publish output under
# out/pub/Functions.WorkerProxy/release_linux-x64.

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app

# Native AOT currently produces one required runtime artifact. Revisit if the publish layout changes.
COPY Azure.Functions.WorkerProxy ./

# Provision the serving PEM certificate/key before starting this executable (see README.md).
# HTTP-capable workers require an allocated HTTPS origin through assignment or trusted configuration.
ENV WORKERPROXY__MANAGEMENTPORT=80 \
    WORKERPROXY__HTTPPORT=28080 \
    WORKERPROXY__RUNTIMEGRPCPORT=50053 \
    WORKERPROXY__WORKERGRPCPORT=50054
EXPOSE 80 28080 50053 50054
ENTRYPOINT ["./Azure.Functions.WorkerProxy"]
