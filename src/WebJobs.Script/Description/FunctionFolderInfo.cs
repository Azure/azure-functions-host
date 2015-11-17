// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionFolderInfo
    {
        public string Name { get; set; }
        public string Source { get; set; }
        public JObject Configuration { get; set; }
    }
}
