// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class JsonSerializers
    {
        /// <summary>
        /// JsonSerializerSettings instance that disables date parsing.
        /// </summary>
        internal static readonly JsonSerializerSettings NoDateParsingSettings = new()
        {
            DateParseHandling = DateParseHandling.None
        };

        /// <summary>
        /// JsonSerializer instance that disables date parsing.
        /// </summary>
        internal static readonly JsonSerializer NoDateParsingSerializer = JsonSerializer.Create(NoDateParsingSettings);
    }
}
