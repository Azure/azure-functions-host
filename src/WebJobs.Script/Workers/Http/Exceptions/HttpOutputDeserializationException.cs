// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class HttpOutputDeserializationException : Exception
    {
        public HttpOutputDeserializationException(string message, string actualException) : base($"Message: {message} ActualException: {actualException}")
        {
        }
    }
}
