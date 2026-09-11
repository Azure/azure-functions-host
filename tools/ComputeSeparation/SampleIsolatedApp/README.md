# Sample isolated app

A small .NET isolated Functions app for manually trying compute separation. It runs as a worker alongside a separate Functions Host and WorkerProxy.

## Run

Follow the [ComputeSeparation run instructions](../README.md), select the `project` or `container` launch profile, and press F5.

This project does not start its own Functions Host. The Aspire tool starts the Host and WorkerProxy and links the worker automatically in both modes.

## Try the function

Once the Host has finished starting, send a GET or POST request to the **Host's** `/api/hello` endpoint. The anonymous `Hello` function returns HTTP 200 with this plain-text body:

```text
Hello from the BYOC .NET isolated worker.
```
