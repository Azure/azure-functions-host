// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ScriptObjectResult : ObjectResult
    {
        public ScriptObjectResult(object value, IDictionary<string, object> headers) : base(value)
        {
            Headers = headers;
            string contentType = null;
            if (value != null &&
                (headers?.TryGetValue("content-type", out contentType, ignoreCase: true) ?? false) &&
                MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue mediaType))
            {
                ContentTypes.Add(mediaType);
            }
        }

        public IDictionary<string, object> Headers { get; }

        internal void AddResponseHeaders(HttpContext context)
        {
            HttpResponse response = context.Response;

            if (Headers != null)
            {
                foreach (var header in Headers)
                {
                    if (response.Headers.ContainsKey(header.Key))
                    {
                        Utility.AccumulateDuplicateHeader(response.HttpContext, header.Key);
                    }
                    else
                    {
                        response.Headers.Add(header.Key, header.Value.ToString() ?? string.Empty);
                    }
                }
            }
        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            AddResponseHeaders(context.HttpContext);
            await base.ExecuteResultAsync(context);
        }
    }
}
