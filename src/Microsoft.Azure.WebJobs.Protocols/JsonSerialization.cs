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
    /// <summary>Provides the standard <see cref="JsonSerializerSettings"/> used by protocol data.</summary>
    public static class JsonSerialization
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
            // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
            DateParseHandling = DateParseHandling.DateTimeOffset,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private static readonly JsonSerializer _serializer = JsonSerializer.CreateDefault(_settings);

        /// <summary>Gets the standard <see cref="JsonSerializerSettings"/> used by protocol data.</summary>
        public static JsonSerializerSettings Settings
        {
            get { return _settings; }
        }

        internal static JsonSerializer Serializer
        {
            get { return _serializer; }
        }

        internal static void ApplySettings(JsonReader reader)
        {
            if (reader == null)
            {
                return;
            }

            reader.Culture = _settings.Culture;
            reader.DateFormatString = _settings.DateFormatString;
            reader.DateParseHandling = _settings.DateParseHandling;
            reader.DateTimeZoneHandling = _settings.DateTimeZoneHandling;
            reader.FloatParseHandling = _serializer.FloatParseHandling;
        }

        internal static void ApplySettings(JsonWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            writer.Culture = _settings.Culture;
            writer.DateFormatHandling = _settings.DateFormatHandling;
            writer.DateFormatString = _settings.DateFormatString;
            writer.DateTimeZoneHandling = _settings.DateTimeZoneHandling;
            writer.FloatFormatHandling = _settings.FloatFormatHandling;
            writer.Formatting = _settings.Formatting;
            writer.StringEscapeHandling = _settings.StringEscapeHandling;
        }

        internal static JsonTextReader CreateJsonTextReader(TextReader reader)
        {
            JsonTextReader jsonReader = new JsonTextReader(reader);
            ApplySettings(jsonReader);
            return jsonReader;
        }

        internal static JsonTextWriter CreateJsonTextWriter(TextWriter textWriter)
        {
            JsonTextWriter jsonWriter = new JsonTextWriter(textWriter);
            ApplySettings(jsonWriter);
            return jsonWriter;
        }

        internal static JObject ParseJObject(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }

            using (StringReader stringReader = new StringReader(json))
            using (JsonTextReader jsonReader = CreateJsonTextReader(stringReader))
            {
                JObject parsed = JObject.Load(jsonReader);

                // Behave as similarly to JObject.Parse as possible (except for the settings used).
                if (jsonReader.Read() && jsonReader.TokenType != JsonToken.Comment)
                {
                    throw new JsonReaderException("Invalid content found after JSON object.");
                }

                return parsed;
            }
        }
    }
}
