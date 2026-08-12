# WorkerProxy runtime image for the compute-separation worker pod.
# The build context contains this Dockerfile and the authenticated linux-x64 Native AOT publish
# under .workerproxy-publish/. The WorkerProxy CI job constructs that minimal context.

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app

COPY .workerproxy-publish/ ./

EXPOSE 80
ENTRYPOINT ["./Microsoft.Azure.Functions.WorkerProxy"]
