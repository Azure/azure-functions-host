---
name: "Bug report"
about: Create a report to help us improve

---

#### Check for a solution in the Azure portal
For issues in production, please check for a solution to common issues in the Azure portal before opening a bug. In the Azure portal, navigate to your function app => `Platform features` => `Diagnose and solve problems` and the relevant dashboards before opening your issue.

<!-- 
Please provide a succinct description of the issue. Please make an effort to fill in the all the sections below or we may close your issue for being low quality. 
-->

#### Investigative information

Please provide the following:

- Timestamp:
- Function App version (1.0 or 2.0):
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
