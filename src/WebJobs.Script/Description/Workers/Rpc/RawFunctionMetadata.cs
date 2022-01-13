// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawFunctionMetadata
    {
        public FunctionMetadata Metadata { get; set; }

        public IEnumerable<string> Bindings { get; set; }

        public string RetryOptions { get; set; }

        public string ConfigurationSource { get; set; }

        public bool UseDefaultMetadataIndexing { get; set; } = true;
    }
}
