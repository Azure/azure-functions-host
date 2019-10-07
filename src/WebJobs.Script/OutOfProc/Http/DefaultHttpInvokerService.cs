// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public class DefaultHttpInvokerService : IHttpInvokerService
    {
        private readonly HttpClient _httpClient;

        public DefaultHttpInvokerService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public Task GetInvocationResponse(ScriptInvocationContext scriptInvocationContext)
        {
            throw new NotImplementedException();
        }
    }
}
