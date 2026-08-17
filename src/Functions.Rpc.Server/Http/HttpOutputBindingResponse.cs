// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpOutputBindingResponse
    {
        public string StatusCode { get; set; }

        public string Status { get; set; }

        public object Body { get; set; }

        public IDictionary<string, object> Headers { get; set; }
    }
}
