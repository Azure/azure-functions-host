// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class JObjectExtensions
    {
        public static JObject ToCamelCase(this JObject obj)
        {
            var camelObj = new JObject();
            foreach (var property in obj.Properties())
            {
                string camelCaseName = property.Name.CamelCaseString();
                if (camelCaseName != null)
                {
                    camelObj[camelCaseName] = property.Value.ToCamelCaseJToken();
                }
            }
            return camelObj;
        }

        public static string CamelCaseString(this string str)
        {
            if (str != null)
            {
                if (str.Length < 1)
                {
                    return str.ToLower();
                }
                else
                {
                    return str.Substring(0, 1).ToLower() + str[1..];
                }
            }
            return str;
        }

        private static JToken ToCamelCaseJToken(this JToken obj)
        {
            switch (obj.Type)
            {
                case JTokenType.Object:
                    return ((JObject)obj).ToCamelCase();
                case JTokenType.Array:
                    return new JArray(((JArray)obj).Select(x => x.ToCamelCaseJToken()));
                default:
                    return obj.DeepClone();
            }
        }
    }
}
