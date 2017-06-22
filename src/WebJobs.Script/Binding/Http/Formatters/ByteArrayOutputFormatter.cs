// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Formatters
{
    public class ByteArrayOutputFormatter : IOutputFormatter
    {
        public bool CanWriteResult(OutputFormatterCanWriteContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Object is byte[])
            {
                return true;
            }

            return false;
        }

        public async Task WriteAsync(OutputFormatterWriteContext context)
        {
            using (var stream = new MemoryStream((byte[])context.Object, false))
            {
                var response = context.HttpContext.Response;

                if (context.ContentType != null)
                {
                    response.ContentType = context.ContentType.ToString();
                }

                await stream.CopyToAsync(response.Body);
            }
        }
    }
}
