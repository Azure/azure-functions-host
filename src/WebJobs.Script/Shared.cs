// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class Shared
    {
        internal static class JsonNetSerializerSettings
        {
            internal static readonly JsonSerializerSettings NoDateParsing = new()
            {
                DateParseHandling = DateParseHandling.None
            };
        }

        internal static class JsonNetSerializers
        {
            internal static readonly JsonSerializer NoDateParsingSerializer =
                        JsonSerializer.Create(JsonNetSerializerSettings.NoDateParsing);
        }
    }
}
