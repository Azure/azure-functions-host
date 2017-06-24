// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostLogEntry
    {
        /// <summary>
        /// Gets or sets the log level.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public System.Diagnostics.TraceLevel Level { get; set; }

        /// <summary>
        /// Gets or sets the name of the function the log entry is for.
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Gets or sets the source of the log entry.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the log message.
        /// </summary>
        public string Message { get; set; }
    }
}