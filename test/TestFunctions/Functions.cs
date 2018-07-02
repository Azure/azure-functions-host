// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;

namespace TestFunctions
{
    public static class Functions
    {
        [FunctionName("Function1")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            string name = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
                .Value;

            if (name == null)
            {
                // Get request body
                dynamic data = await req.Content.ReadAsAsync<object>();
                name = data?.name;
            }

            return name == null
                ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
                : req.CreateResponse(HttpStatusCode.OK, "Hello " + name);
        }

        // this function intentionally doesn't have the FunctionName attribute on
        // it for testing purposes
        public static void QueueTrigger([QueueTrigger("test")] string message, TraceWriter log)
        {
            log.Info($"C# Queue trigger function processed message '{message}'.");
        }

        // this function is invalid because it doesn't have a corresponding
        // function directory or function.json
        public static void InvalidFunction([QueueTrigger("test")] string message, TraceWriter log)
        {
            log.Info($"C# Queue trigger function processed message '{message}'.");
        }
    }
}
