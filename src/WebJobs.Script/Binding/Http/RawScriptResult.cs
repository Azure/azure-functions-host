// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Formatters;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class RawScriptResult : IActionResult
    {
        private static readonly IOutputFormatter[] _outputFormatters = new IOutputFormatter[]
        {
            new StreamOutputFormatter(),
            new StringOutputFormatter(),
            new ByteArrayOutputFormatter()
        };

        public RawScriptResult(int? statusCode, object content)
        {
            StatusCode = statusCode;
            Content = content;
        }

        public int? StatusCode { get; set; }

        public object Content { get; set; }

        public IDictionary<string, object> Headers { get; set; }

        public List<Tuple<string, string, CookieOptions>> Cookies { get; set; }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            HttpResponse response = context.HttpContext.Response;

            if (Headers != null)
            {
                foreach (var header in Headers)
                {
                    if (header.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (header.Value == null)
                        {
                            throw new InvalidOperationException("content-type header cannot be null");
                        }
                        response.ContentType = header.Value.ToString();
                    }
                    else
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

            if (StatusCode != null)
            {
                response.StatusCode = StatusCode.Value;
            }

            if (Cookies != null)
            {
                foreach (var cookie in Cookies)
                {
                    if (cookie.Item3 != null)
                    {
                        response.Cookies.Append(cookie.Item1, cookie.Item2, cookie.Item3);
                    }
                    else
                    {
                        response.Cookies.Append(cookie.Item1, cookie.Item2);
                    }
                }
            }

            await WriteResponseBodyAsync(response, Content);
        }

        private async Task WriteResponseBodyAsync(HttpResponse response, object content)
        {
            if (content is ExpandoObject expandoContent)
            {
                content = Utility.ToJson(expandoContent, Formatting.None);
            }

            var formatterContext = CreateFormatterContext(response, content);

            IOutputFormatter selectedFormatter = SelectedFormatter(formatterContext);

            if (selectedFormatter == null)
            {
                // We were unable to locate a formatter with the provided type, to maintain the original behavior
                // we'll convert the object to string and get a formatter
                content = content?.ToString() ?? string.Empty;

                formatterContext = CreateFormatterContext(response, content);
                selectedFormatter = SelectedFormatter(formatterContext);
            }

            formatterContext.ContentType = response.ContentType;

            await selectedFormatter.WriteAsync(formatterContext);
        }

        private static OutputFormatterWriteContext CreateFormatterContext(HttpResponse response, object content)
        {
            var context = new OutputFormatterWriteContext(
                response.HttpContext,
                (s, e) => new HttpResponseStreamWriter(s, e),
                content?.GetType(),
                content);

            return context;
        }

        private static IOutputFormatter SelectedFormatter(OutputFormatterWriteContext formatterContext)
        {
            return _outputFormatters.FirstOrDefault(f => f.CanWriteResult(formatterContext));
        }
    }
}
