#!/bin/bash
set -e

echo "[Entrypoint] Starting Worker container..."
echo "[Entrypoint] Worker ID: $WORKER_ID"
echo "[Entrypoint] Functions URI: $FUNCTIONS_URI"

# Start Sidecar in background
echo "[Entrypoint] Starting Sidecar..."
cd /app/sidecar
dotnet WorkerModel.Sidecar.dll &
SIDECAR_PID=$!

# Wait for Sidecar to be ready
echo "[Entrypoint] Waiting for Sidecar to be ready..."
sleep 2

# Check if Sidecar is running
if ! kill -0 $SIDECAR_PID 2>/dev/null; then
    echo "[Entrypoint] Sidecar failed to start!"
    exit 1
fi

# Start Wrapper (which starts FunctionsNetHost)
echo "[Entrypoint] Starting Wrapper..."
cd /app/wrapper
./WorkerModel.Wrapper &
WRAPPER_PID=$!

# Function to cleanup on exit
cleanup() {
    echo "[Entrypoint] Shutting down..."
    
    # Stop Wrapper first (it will stop FunctionsNetHost)
    if kill -0 $WRAPPER_PID 2>/dev/null; then
        kill -TERM $WRAPPER_PID
        wait $WRAPPER_PID 2>/dev/null || true
    fi
    
    # Stop Sidecar
    if kill -0 $SIDECAR_PID 2>/dev/null; then
        kill -TERM $SIDECAR_PID
        wait $SIDECAR_PID 2>/dev/null || true
    fi
    
    echo "[Entrypoint] Shutdown complete"
}

trap cleanup SIGTERM SIGINT

# Wait for either process to exit
wait -n $SIDECAR_PID $WRAPPER_PID

# One of them exited, cleanup and exit
echo "[Entrypoint] A process exited, shutting down container..."
cleanup
exit 1
