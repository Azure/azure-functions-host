// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    internal static class JTokenExtensions
    {
        public static string ToJsonString(this JToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException("token");
            }

            // The following code is different from token.ToString(), which special-cases null to return "" instead of
            // "null".
            using (StringWriter stringWriter = new StringWriter())
            using (JsonWriter jsonWriter = JsonSerialization.CreateJsonTextWriter(stringWriter))
            {
                token.WriteTo(jsonWriter);
                jsonWriter.Flush();
                stringWriter.Flush();
                return stringWriter.ToString();
            }
        }
    }
}
