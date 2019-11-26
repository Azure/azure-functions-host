// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class HttpBodyControlOptions : IOptionsFormatter
    {
        public bool AllowSynchronousIO { get; set; }

        public string Format()
        {
            var options = new JObject
            {
                { nameof(AllowSynchronousIO), AllowSynchronousIO }
            };

            return options.ToString(Formatting.Indented);
        }
    }
}