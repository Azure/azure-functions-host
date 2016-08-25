// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class ExpandoObjectJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ExpandoObject);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            WriteExpandoObject(writer, (ExpandoObject)value, serializer);
        }

        private static void WriteExpandoObject(JsonWriter writer, ExpandoObject value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            var values = value as IDictionary<string, object>;
            foreach (var pair in values)
            {
                if (pair.Value != null && pair.Value is Delegate)
                {
                    // skip types like Functions, etc.
                    continue;
                }

                writer.WritePropertyName(pair.Key);

                if (pair.Value is ExpandoObject)
                {
                    WriteExpandoObject(writer, (ExpandoObject)pair.Value, serializer);
                }
                else
                {
                    serializer.Serialize(writer, pair.Value);
                }
            }

            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
