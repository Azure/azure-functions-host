// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal sealed class ResponseCompressionOptions : IOptionsFormatter
    {
        public bool EnableResponseCompression { get; set; }

        public string Format()
        {
            var options = new JObject
            {
                { nameof(ResponseCompressionOptions), EnableResponseCompression }
            };

            return options.ToString(Formatting.Indented);
        }
    }
}
