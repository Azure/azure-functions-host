// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.Tests
{
    public static partial class Functions
    {
        public static object InvokeData { get; set; }

        public static async Task WebHook(HttpRequestMessage request, TraceWriter traceWriter)
        {
            string body = await request.Content.ReadAsStringAsync();

            InvokeData = body;

            traceWriter.Info(string.Format("C# WebHook function received message '{0}'", body));
        }
    }
}
