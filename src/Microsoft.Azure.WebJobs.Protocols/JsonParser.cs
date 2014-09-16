// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Protocols
{
    internal class JsonParser
    {
        public static JObject ParseWithDateTimeOffset(string jsonText)
        {
            if (jsonText == null)
            {
                throw new ArgumentNullException("jsonText");
            }

            using (StringReader stringReader = new StringReader(jsonText))
            using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
            {
                // Parses dates with offset as DateTimeOffset 
                // rather that just DateTime (useful for rountripping)
                jsonReader.DateParseHandling = DateParseHandling.DateTimeOffset;

                return JObject.ReadFrom(jsonReader) as JObject;
            }
        }
    }
}
