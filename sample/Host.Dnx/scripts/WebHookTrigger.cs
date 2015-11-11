// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Host
{
    public static partial class Functions
    {
        public static async Task WebHookTrigger(HttpRequestMessage request, TraceWriter traceWriter)
        {
            string body = await request.Content.ReadAsStringAsync();

            traceWriter.Info(string.Format("C# WebHookTrigger function received message '{0}'", body));
        }
    }
}
