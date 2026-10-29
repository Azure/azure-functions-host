# Sample isolated app

A small .NET isolated Functions app for trying compute separation. It contains both the HTTP `Hello` function and queue-triggered `QueueEcho`,
and runs as a worker alongside a separate Functions Host and WorkerProxy.

## Run

Prepare the matching local worker packages using the [ComputeSeparation run instructions](../README.md), select the `project` or `container`
launch profile, and press F5. Every profile starts an Azurite container, so Docker is required in project mode too.

This project does not start its own Functions Host. The Aspire tool starts the Host and WorkerProxy and links the worker automatically in both modes. For manual linking of two workers, select `project-placeholder-manual-multiple-workers` and follow `compute-separation.http`.

The sample keeps the Worker SDK's build and metadata generation, but disables its Core Tools launch targets and Visual Studio's `AzureFunctions` project capability so both launch the worker directly. Reload the sample project if Visual Studio previously loaded it as an Azure Functions project.

## Try HTTP

Once the Host has finished starting, send a GET or POST request to the **Host's** `/api/hello` endpoint. The anonymous `Hello` function returns HTTP 200 with this plain-text body:

```text
Hello from the BYOC .NET isolated worker. Worker ID: aspire-<worker-guid>
```

The ID is the actual `--functions-worker-id` supplied at startup, also used when linking this worker to the Host.
Each worker's ID is generated independently. Aspire accepts overrides through `ComputeSeparation:WorkerId` and
`ComputeSeparation:SecondWorker:WorkerId`. Repeated requests show which worker handled each invocation.

## Try queues

Every launch includes `QueueEcho`, which reads `sample-input` and returns the same text through the Host-managed output binding to `sample-output`.
Use the Aspire `queues` resource's connection details in project mode, or the runtime's published `blob` and `queue` endpoints
with Azurite's development account in container mode; see [storage networking](../README.md#storage-networking).
Create the input queue if needed and enqueue a Base64-encoded message. AppHost supplies both queue names and the storage
connection to the Host and each worker.

The optional `ComputeSeparation:VerifyQueueTrigger=true` container run uses unique queues, checks the returned token and `/api/hello`, captures
evidence, and stops its resources automatically. It uses this same sample app, not a separate queue-only deployment.
