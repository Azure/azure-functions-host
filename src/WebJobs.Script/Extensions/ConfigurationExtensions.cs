// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    internal static class ConfigurationExtensions
    {
        /// <summary>
        /// Convert IConfiguration object to the JToken object with a section
        /// </summary>
        /// <param name="configuration">configuration object</param>
        /// <param name="section">Section that you want to fetch.</param>
        /// <returns>JToken representation of the configuration</returns>
        public static JToken Convert(this IConfiguration configuration, string section)
        {
            return Parse(configuration.GetSection(section));
        }

        private static JToken Parse(IConfigurationSection section)
        {
            var jObject = new JObject();

            var key = section.Key;
            var children = section.GetChildren();
            if (children.Count() == 0 && section.Value != null)
            {
                return jObject[key] = GetLowerCaseForBoolean(section.Value);
            }
            foreach (var child in children)
            {
                jObject.Add(child.Key, Parse(child));
            }
            return jObject;
        }

        // TODO This method is mitigation in case the caller of the SyncTrigger using DataContractJsonSerializer as  a serialization method.
        // This extension method will be removed once we implement the Default Value Solution.
        private static string GetLowerCaseForBoolean(string value)
        {
            if (value != null && (value == "True" || value == "False"))
            {
                return value.ToLower();
            }
            return value;
        }
    }
}
