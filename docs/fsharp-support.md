Hi @christopheranderson  @fabiocav , here is my take on a first-cut of the feature specification.  Please edit into the top of this issue, or delete the template above and edit directly here ( then I can edit too)

## Status

#### Design questions:

- [x] ``#load`` directives in F# functions? 

  Resolution: Yes, these are allowed

- [x] Do we tuple returns instead of byref outputs to get a more idiomatic F# signature?

  Resolution: no. Or at least not yet. Right now we are getting a lot of value from parity and symmetry with C#.  It means that the vast majority of the implementation stays identical.

- [x] Do we use ``Async`` instead of ``Task`` to get a more idiomatic F# signature?

  Resolution: again no, or not yet, for the simplicity arising from parity and symmetry with C#.

- [x] Nuget package references?

  Resolution: these come from package.json, we should get these automatically, [see the C# specification](https://azure.microsoft.com/en-us/documentation/articles/functions-reference-csharp/))

- [x] Should we turn on optimization when debugging is off

  Resolution: No, not yet. C# Azure Functions doesn't do this, so we won't do this yet for F#.  Eventually it will be added.

#### Core Implementation

- [x] Bring F# tests up to parity with C# tests (FSharpEndToEndTests.cs)
- [ ] Get CI passing for ``fsharp`` branch by disabling F# tests
- [ ] Progressively enable simple F# tests in CI of ``fsharp`` branch
- [ ] Allow debugging to be turned off. In code: ``TODO: Get debug flag from context. Set to true for now.``
- [ ] Get nuget package references working
- [ ] Turn off optimizations by default
- [ ] Use latest FSharp.Compiler.Service.dll (for F# 4.1 features)

#### Core QA

- [ ] Check a range of failure conditions, especially ones that are "catastrophic" to compilation: Bad DLL references, Bad nuget package references, Bad ``#load`` references, Basic type error, Basic runtime error
- [ ] Check the off-by one conditions for diagnostic error reporting, especially when mapping from
FSharp.Compiler.Service line/column to the Azure Functions diagnostics

#### Double checking QA 

Based on the implementation we don't expect problems with these but should double check:

- [ ] Double check F# type providers (FSharp.Data) can be used 
- [ ] Double check what happens with exceptions at runtime 
- [ ] Double check implicitly referenced DLLs are the same as C#
- [ ] Double check symbols are being generated correctly and debugging works.  Code seems to indicate they are.
- [ ] Double check "hot runtime" for F#. Should be identical to C#.

#### Delivery 

- [ ] Bring F# samples up to parity with C# tests (samples\...)
- [ ] Add basic F# docs to website
- [ ] Add basic F# samples to user-visible samples collection
- [ ] Write blog post announcement with samples


## Detailed Specification

#### Function format

Here is a sample function:

```fsharp
open System
open Microsoft.Azure.WebJobs.Host

let Run (input: string, output: byref<string>, log: TraceWriter) =
    log.Verbose(sprintf "F# ApiHub trigger function processed a file...");

    output <- input
```

Functions are authored as ``.fsx`` files.  This is done on the assumption that functions are often small, script-like pieces of code and can benefit from the "implicit project" of a script through ``#r`` and ``#load`` references.  

#### Function Signatures

Functions follow the C# signature exactly.

* Functions currently report outputs through the use of byref parameters.

* The names of the parameters must match those in the config.

* The optional logging parameter is automagically inserted by the Azure Functions host.

> Question: do we want to use a more idiomatic F# signature, e.g. tuple returns.  However tuple returns are problematic because names are not avaiable for the different fields and positions would need to be used.

> Question: could we allow different signatures in later versions?  What's the upgrade story?


#### DLL, Nuget and File References

Functions are loaded and executed with an implicit set of DLL references.

The set of implicit DLL references is the same as C#.

> Question: does it make sense to use ``#load`` directives in F# functions?  What is the set of current source code files (i.e. current directory) that we would have access to?  Presumably from function-author perspective this would be the current directory of the script within the GitHub or other source control tree. 

#### Editing Functions


Because functions are scripts, they are usually edited in a tool like Visual Studio or Visual Studio Code, which provides editing support for F# scripts.  However that tool doesn't know the set of implicit references being used by Azure Functions.  For this reason it is useful for the function author to add a declaration like the following to ensure the DLL references are active when using the function:

```fsharp
//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#endif
```

This prelude is optional but is included in the test cases that form part of this PR.


### Data Type formats

As for C#

### Version & Package management

Default references are the same as for C#.

> Question: are nuget package references allowed in F# code and are they resoved correctly.

#### FSharp.Compiler.Service runtime dependency

The component FSharp.Compiler.Service is used at runtime.

#### FSharp.Core compile-time reference and runtime dependency

``WebJobs.Script.Host`` and ``WebJobs.Script`` already include FSharp.Core as a runtime component.
These should use ``FSharp.Core 4.4.0.0`` at runtime and compile-time.  

FSharp.Core is a binary-comatible component and later releases of Azure Functions can move to using a
later version of FSharp.Core at runtime.

There is currently no way to specify the required .NET Framework or specific version of FSharp.Core
as part of the F# ``.fsx`` scripting model.

All compilation and compile-time execution of F# Type Providers operate with the following binding redirect active:

    <bindingRedirect oldVersion="0.0.0.0-4.4.0.0" newVersion="4.4.0.0" />

> Question: what about binding redirects for imported packages?


### Testing/CI

In progress.  Testing should be 1:1 with C# testing for all function definitions.

> TODO: Check ``FSharpEndToEndTests`` against C# equivalents and make sure everything is being tested.

F# tests should be run as part of CI.


### Debugging (Remote and Local)

As for C#.  

> TODO: check that symbols are being generated correctly.

> TODO: resolve this ``TODO: Get debug flag from context. Set to true for now.``

### Hot runtime

As for C#.

### Compilation

The FSharp.Compiler.Service component is used for compilation

### List of samples in https://github.com/Azure/azure-webjobs-sdk-script/ repo

* Each C# sample should be matched by an F# sample, e.g. ``sample/DocumentDB-FSharp/...``

Maintaining this list of samples and keeping the code up-to-date is a significant ongoing cost.


### Compilation Diagnostics

Compilation diagnostics returned by FSharp.Compiler.Service are reported as Azure Functions diagnostics.

Correct compilation diagnostics should be given for a range of failure conditions, especially ones that are "catastrophic" to compilation:

* Bad DLL references
* Bad nuget package references
* Bad ``#load`` references
* Basic type checking errors
* Runtime exceptions

### F# Type Providers

F# Type Providers execute at the time of script compilation. The compilation happens in the script host.
This operates with the same rights and priveleges as script execution.



