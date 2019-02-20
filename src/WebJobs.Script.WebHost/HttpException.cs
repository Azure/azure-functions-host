// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HttpException : Exception
    {
        public HttpException(int httpStatusCode)
        {
            StatusCode = httpStatusCode;
        }

        public HttpException(HttpStatusCode httpStatusCode)
        {
            StatusCode = (int)httpStatusCode;
        }

        public HttpException(int httpStatusCode, string message) : base(message)
        {
            StatusCode = httpStatusCode;
        }

        public HttpException(HttpStatusCode httpStatusCode, string message) : base(message)
        {
            StatusCode = (int)httpStatusCode;
        }

        public HttpException(int httpStatusCode, string message, Exception inner) : base(message, inner)
        {
            StatusCode = httpStatusCode;
        }

        public HttpException(HttpStatusCode httpStatusCode, string message, Exception inner) : base(message, inner)
        {
            StatusCode = (int)httpStatusCode;
        }

        public int StatusCode { get; }
    }
}
