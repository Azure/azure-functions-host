# WorkerProxy runtime image for the compute-separation worker pod.
# The build context contains this Dockerfile and the authenticated linux-x64 Native AOT publish
# under .workerproxy-publish/. The WorkerProxy CI job constructs that minimal context.

# 1ES pipelines support building a local image but do not currently provide a supported task
# for running that image in the same job; raw docker commands in scripts are also disallowed.
# Keep the startup probe in a build stage for now, and revisit this when 1ES supports a
# first-class local-image smoke test.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS smoke
WORKDIR /app

COPY .workerproxy-publish/ ./

SHELL ["/bin/bash", "-c"]
RUN set -euo pipefail; \
    logFile=/tmp/workerproxy.log; \
    ./Azure.Functions.WorkerProxy --management-port 8080 >"$logFile" 2>&1 & \
    workerProxyPid=$!; \
    cleanup() { \
        exitCode=$?; \
        trap - EXIT; \
        kill "$workerProxyPid" >/dev/null 2>&1 || true; \
        wait "$workerProxyPid" >/dev/null 2>&1 || true; \
        if (( exitCode != 0 )); then cat "$logFile"; fi; \
        exit "$exitCode"; \
    }; \
    trap cleanup EXIT; \
    for attempt in {1..60}; do \
        if { exec 3<>/dev/tcp/127.0.0.1/8080; } 2>/dev/null; then \
            printf 'GET /admin/instance/ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3; \
            read -r statusLine <&3 || true; \
            exec 3>&- 3<&-; \
            if [[ "$statusLine" == *" 200 "* ]]; then exit 0; fi; \
        fi; \
        sleep 1; \
    done; \
    exit 1

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app

COPY --from=smoke /app/ ./

EXPOSE 80
ENTRYPOINT ["./Azure.Functions.WorkerProxy"]
