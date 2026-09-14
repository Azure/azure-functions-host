# Sample isolated app

A small .NET isolated Functions app for manually trying compute separation. It runs as a worker alongside a separate Functions Host and WorkerProxy.

## Run

Follow the [ComputeSeparation run instructions](../README.md), select the `project` or `container` launch profile, and press F5.

This project does not start its own Functions Host. The Aspire tool starts the Host and WorkerProxy and links the worker automatically in both modes.

The sample keeps the Worker SDK's build and metadata generation, but disables its Core Tools launch targets and Visual Studio's `AzureFunctions` project capability so both launch the worker directly. Reload the sample project if Visual Studio previously loaded it as an Azure Functions project.

## Try the function

Once the Host has finished starting, send a GET or POST request to the **Host's** `/api/hello` endpoint. The anonymous `Hello` function returns HTTP 200 with this plain-text body:

```text
Hello from the BYOC .NET isolated worker.
```
