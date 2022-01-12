// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class WebJobsExtensionOptionSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new WebJobsExtensionOptionProvider();

        public class WebJobsExtensionOptionProvider : ConfigurationProvider
        {
            private Stack<string> _path;

            public WebJobsExtensionOptionProvider()
            {
                WebJobsExtensionOptionRegistry.Subscribe(nameof(WebJobsExtensionOptionProvider), Load);
            }

            public override void Load()
            {
                _path = new Stack<string>();
                var json = WebJobsExtensionOptionRegistry.GetOptions();
                ProcessObject(json);
                OnReload();
            }

            private void ProcessObject(JObject json)
            {
                foreach (var property in json.Properties())
                {
                    _path.Push(property.Name);
                    ProcessProperty(property);
                    _path.Pop();
                }
            }

            private void ProcessProperty(JProperty property)
            {
                ProcessToken(property.Value);
            }

            private void ProcessToken(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        ProcessObject(token.Value<JObject>());
                        break;
                    case JTokenType.Array:
                        ProcessArray(token.Value<JArray>());
                        break;

                    case JTokenType.Integer:
                    case JTokenType.Float:
                    case JTokenType.String:
                    case JTokenType.Boolean:
                    case JTokenType.Null:
                    case JTokenType.Date:
                    case JTokenType.Raw:
                    case JTokenType.Bytes:
                    case JTokenType.TimeSpan:
                        string key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter + "extensions" + ConfigurationPath.KeyDelimiter + ConfigurationPath.Combine(_path.Reverse());
                        if (Data.ContainsKey(key))
                        {
                            Data.Remove(key);
                        }
                        Data[key] = token.Value<JValue>().ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        break;
                }
            }

            private void ProcessArray(JArray array)
            {
                for (int i = 0; i < array.Count(); i++)
                {
                    _path.Push(i.ToString());
                    ProcessToken(array[i]);
                    _path.Pop();
                }
            }
        }
    }
}
