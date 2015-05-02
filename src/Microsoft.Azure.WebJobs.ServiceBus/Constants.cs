// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal static class Constants
    {
        private static JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
            // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
            DateParseHandling = DateParseHandling.DateTimeOffset,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static JsonSerializerSettings JsonSerializerSettings
        {
            get
            {
                return _serializerSettings;
            }
        }
    }
}
