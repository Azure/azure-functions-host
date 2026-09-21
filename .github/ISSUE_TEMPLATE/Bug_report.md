---
name: "Bug report"
about: Report a bug in the Azure Functions Host / Runtime

---

<!-- 
⚠️ IS THIS THE RIGHT PLACE FOR YOUR ISSUE? ⚠️

This repository is for the Azure Functions **Host / Runtime** only. Please open an issue here
only for clear, reproducible bugs or code issues in the Functions Host / Runtime itself.

This is NOT the right place for Azure Functions **platform-level** concerns, such as:
- Deployments (including remote build, OneDeploy, `RuntimeFailed`/`ExpectationFailed` controller errors)
- Scaling, cold start, or instance/availability behavior
- ARM / control plane operations, provisioning, or configuration
- Portal, billing, quotas, networking, or storage infrastructure

For platform-level issues, please open a formal Azure support case so the appropriate team can
investigate with access to your resources and telemetry:
https://learn.microsoft.com/azure/azure-portal/supportability/how-to-create-azure-support-request

Support cases are the fastest path to resolution for these concerns and let us handle
resource-specific data privately. Platform issues opened here will typically be redirected to support.

If your issue isn't a Host/Runtime bug and isn't a clear platform-support case (for example, it
belongs to a binding/trigger extension, the tooling, or another part of Azure Functions), please
use the guidance here to find and file in the correct repository:
https://github.com/Azure/Azure-Functions#issues--feature-requests
-->

#### Check for a solution in the Azure portal
For issues in production, please check for a solution to common issues in the Azure portal before opening a bug. In the Azure portal, navigate to your function app, select `Diagnose and solve problems` from the left, and view relevant dashboards before opening your issue.

<!-- 
Please provide a succinct description of the issue. Please make an effort to fill in the all the sections below or we may close your issue for being low quality. 
-->

#### Investigative information

Please provide the following:

- Timestamp:
- Function App version:
- Function App name:
- Function name(s) (as appropriate):
- Invocation ID:
- Region:

<!-- 
If you don't want to share your Function App name or Functions names on GitHub, please be sure to provide your Invocation ID, Timestamp, and Region - we can use this to look up your Function App/Function. Provide an invocation id per Function. See the [wiki](https://github.com/Azure/azure-webjobs-sdk-script/wiki/Sharing-Your-Function-App-name-privately) for more details. 
-->

#### Repro steps

Provide the steps required to reproduce the problem:

<!--
Example: 

1. Step A
2. Step B
-->

#### Expected behavior

Provide a description of the expected behavior.

<!--
Example:

 - After I perform step B, the lights in the house should turn off.
-->

#### Actual behavior

Provide a description of the actual behavior observed.

<!--
Example:

- Step B actually causes my cat to meow for some reason.
-->

#### Known workarounds

Provide a description of any known workarounds.

<!--
Example:

- Turn off the circuit breaker for the lights.
-->

#### Related information 

Provide any related information 

* Programming language used 
* Links to source
* Bindings used
<!-- Uncomment this if you want to include your source (wrap it in details to make browsing easier)
<details>
<summary>Source</summary>

```csharp
public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.AuthLevelValue, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    // parse query parameter
    string name = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
        .Value;

    // Get request body
    dynamic data = await req.Content.ReadAsAsync<object>();

    // Set name to query string or body data
    name = name ?? data?.name;

    return name == null
        ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
        : req.CreateResponse(HttpStatusCode.OK, "Hello " + name);
}
```
</details>
-->
