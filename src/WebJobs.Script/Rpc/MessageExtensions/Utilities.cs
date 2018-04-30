// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal static class Utilities
    {
        public static object ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
        {
            if (inputMessage == null)
            {
                return null;
            }

            dynamic expando = new ExpandoObject();
            expando.method = inputMessage.Method;
            expando.query = inputMessage.Query as IDictionary<string, string>;
            expando.statusCode = inputMessage.StatusCode;
            expando.headers = inputMessage.Headers.ToDictionary(p => p.Key, p => (object)p.Value);
            expando.enableContentNegotiation = inputMessage.EnableContentNegotiation;

            if (inputMessage.Body != null)
            {
                expando.body = inputMessage.Body.ToObject();
            }
            return expando;
        }
    }
}