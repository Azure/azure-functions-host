// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ProxyMetadata : FunctionMetadata
    {
        public HttpMethod Method { get; set; }

        public string UrlTemplate { get; set; }
    }
}
