using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal static class ProxyHttpExtensions
    {
        public static string Serialize(HttpResponseMessage response)
        {
            return SerializeObject(response);
        }

        public static string Serialize(HttpRequestMessage request)
        {
            return SerializeObject(request);
        }

        public static bool TryDeserialize(string value, out HttpRequestMessage request)
        {
            JToken jToken = JToken.Parse(value);
            return TryDeserialize(jToken, out request);
        }

        public static bool TryDeserialize(JToken jsonToken, out HttpRequestMessage request)
        {
            request = new HttpRequestMessage();

            if (jsonToken["Method"] != null)
            {
                request.Method = JsonConvert.DeserializeObject<HttpMethod>(jsonToken["Method"].ToString());
            }
            else
            {
                return false;
            }

            if (jsonToken["RequestUri"] != null)
            {
                Uri uri = null;
                if (Uri.TryCreate(jsonToken["RequestUri"].ToString(), UriKind.Absolute, out uri))
                {
                    request.RequestUri = uri;
                }
            }
            else
            {
                return false;
            }

            var content = jsonToken["Content"];
            if (content != null && content.HasValues)
            {
                DeserializeContent(request, content);
            }

            if (jsonToken["Headers"] != null && jsonToken["Headers"].HasValues)
            {
                foreach (var header in jsonToken["Headers"].Children())
                {
                    AddHeader(request.Headers, header);
                }
            }

            return true;
        }

        public static bool TryDeserialize(string value, out HttpResponseMessage response)
        {
            JObject jObject = JObject.Parse(value);

            response = new HttpResponseMessage();

            HttpStatusCode statusCode;
            if (jObject["StatusCode"] != null && Enum.TryParse(jObject["StatusCode"].ToString(), out statusCode))
            {
                response.StatusCode = statusCode;
            }
            else
            {
                return false;
            }

            if (jObject["ReasonPhrase"] != null)
            {
                response.ReasonPhrase = jObject["ReasonPhrase"].ToString();
            }

            var content = jObject["Content"];
            if (content != null && content.HasValues)
            {
                DeserializeContent(response, content);
            }

            if (jObject["Headers"] != null && jObject["Headers"].HasValues)
            {
                foreach (var header in jObject["Headers"].Children())
                {
                    AddHeader(response.Headers, header);
                }
            }

            if (jObject["RequestMessage"] != null && jObject["RequestMessage"].HasValues)
            {
                HttpRequestMessage requestMessage = null;
                if (TryDeserialize(jObject["RequestMessage"], out requestMessage))
                {
                    response.RequestMessage = requestMessage;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public static void DeserializeContent(dynamic req, JToken content)
        {
            var headers = new List<JToken>();

            MediaTypeHeaderValue contentType = null;
            string charSet = null;

            if (content["Headers"] != null && content["Headers"].HasValues)
            {
                foreach (var header in content["Headers"].Children())
                {
                    if (header["Key"] != null && header["Key"].ToString().IndexOf("Content-Type", 0, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (header["Value"] != null && header["Value"].HasValues)
                        {
                            // only getting the first value for content-type, if more they are invalid
                            var contentTypeHeader = header["Value"][0].ToString().Split(';');

                            // ignore invalid content-types
                            if (!MediaTypeHeaderValue.TryParse(contentTypeHeader[0], out contentType))
                            {
                                continue;
                            }

                            if (contentTypeHeader.Length > 1 && contentTypeHeader[1].Trim().StartsWith("charset", StringComparison.OrdinalIgnoreCase))
                            {
                                charSet = contentTypeHeader[1].Trim().Replace("charset=", "");
                            }
                        }
                        continue;
                    }
                    headers.Add(header);
                }
            }

            if (content["Content"] != null)
            {
                if (ShouldUseBase64Encoding(contentType))
                {
                    req.Content = new ByteArrayContent(Convert.FromBase64String(content["Content"].ToString()));
                }
                else
                {
                    req.Content = new StringContent(content["Content"].ToString(), GetCurrentEncoding(charSet));
                }
            }
            else
            {
                req.Content = new StringContent(string.Empty);
            }

            if (contentType != null)
            {
                req.Content.Headers.ContentType = contentType;
                if (!string.IsNullOrWhiteSpace(charSet))
                {
                    req.Content.Headers.ContentType.CharSet = charSet;
                }
            }

            foreach (var header in headers)
            {
                AddHeader(req.Content.Headers, header);
            }
        }

        private static string SerializeObject(object obj)
        {
            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new IgnoreErrorPropertiesResolver(),
                Converters = new List<JsonConverter>() { new HttpContentJsonConverter() }
            };

            string objStr = JsonConvert.SerializeObject(
                obj,
                settings
            );

            return objStr;
        }

        private static void AddHeader(dynamic req, JToken header)
        {
            if (header["Key"] == null)
            {
                return;
            }

            if (header["Value"] != null && header["Value"].HasValues)
            {
                if (header["Value"].Children().Count() > 1)
                {
                    List<string> headerValues = new List<string>();
                    foreach (var value in header["Value"].Children())
                    {
                        headerValues.Add(value.ToString());
                    }
                    req.TryAddWithoutValidation(header["Key"].ToString(), headerValues);
                }
                else
                {
                    req.TryAddWithoutValidation(header["Key"].ToString(), header["Value"].Children().FirstOrDefault().ToString());
                }
            }
            else
            {
                req.TryAddWithoutValidation(header["Key"].ToString(), string.Empty);
            }
        }

        private static bool ShouldUseBase64Encoding(MediaTypeHeaderValue contentType)
        {
            string[] textMediaTypes = {
                "application/x-javascript",
                "application/javascript",
                "application/json",
                "application/xml",
                "application/xhtml+xml",
                "application/x-www-form-urlencoded"
                };

            bool isTextData = false;
            if (contentType != null)
            {
                isTextData = contentType.MediaType != null &&
                    (contentType.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                    textMediaTypes.Any(s => string.Equals(s, contentType.MediaType, StringComparison.OrdinalIgnoreCase)));
            }
            return !isTextData;
        }

        public static Encoding GetCurrentEncoding(HttpContentHeaders headers)
        {
            if (headers != null &&
                headers.ContentType != null)
            {
                return GetCurrentEncoding(headers.ContentType.CharSet);
            }

            return Encoding.UTF8;
        }

        private static Encoding GetCurrentEncoding(string charSet)
        {
            Encoding encoding;
            try
            {
                if (!string.IsNullOrWhiteSpace(charSet))
                {
                    encoding = Encoding.GetEncoding(charSet);
                }
                else
                {
                    encoding = Encoding.UTF8;
                }
            }
            catch (ArgumentException)
            {
                //Invalid char set value, defaulting to UTF8
                encoding = Encoding.UTF8;
            }

            return encoding;
        }

        private class IgnoreErrorPropertiesResolver : DefaultContractResolver
        {
            static List<string> ignoreProperties = new List<string>() { "Properties", "Version" };

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);

                if (ignoreProperties.Contains(property.PropertyName))
                {
                    property.Ignored = true;
                }
                return property;
            }
        }

        private class HttpContentJsonConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                if (objectType == typeof(StringContent) || objectType == typeof(StreamContent) || objectType == typeof(ByteArrayContent))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                // Not needed            
                throw new NotImplementedException();
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                string str = string.Empty;
                HttpContentHeaders headers = null;

                var field = typeof(ByteArrayContent).GetField("content", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    field = typeof(ByteArrayContent).GetField("_content", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (value is StringContent)
                {
                    var content = (StringContent)value;
                    headers = content.Headers;

                    var encoding = ProxyHttpExtensions.GetCurrentEncoding(headers);

                    str = encoding.GetString((byte[])field.GetValue(content));
                }
                else if (value is ByteArrayContent)
                {
                    var content = (ByteArrayContent)value;
                    headers = content.Headers;
                    str = Convert.ToBase64String((byte[])field.GetValue(content));
                }
                else
                {
                    var content = (StreamContent)value;
                    headers = content.Headers;
                    var bytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    str = Convert.ToBase64String(bytes);
                }

                writer.WriteStartObject();
                writer.WritePropertyName("Content");
                writer.WriteValue(str);
                writer.WritePropertyName("Headers");
                writer.WriteStartArray();

                foreach (var header in headers)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Key");
                    writer.WriteValue(header.Key);
                    writer.WritePropertyName("Value");
                    writer.WriteStartArray();
                    foreach (var v in header.Value)
                    {
                        writer.WriteValue(v);
                    }
                    writer.WriteEnd();
                    writer.WriteEndObject();
                }

                writer.WriteEnd();
                writer.WriteEndObject();
            }
        }
    }
}