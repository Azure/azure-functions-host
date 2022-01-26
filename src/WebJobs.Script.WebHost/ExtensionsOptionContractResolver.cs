// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ExtensionsOptionContractResolver : DefaultContractResolver
    {
        private static readonly HashSet<string> _skipSerialization = new HashSet<string>
        {
            "System.Net.IWebProxy",
            "Newtonsoft.Json.JsonSerializerSettings"
        };

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            var propertyType = property.PropertyType;
            if (property != null && property.PropertyName != null)
            {
                property.PropertyName = ConvertCamelCase(property.PropertyName);
            }
            // Avoding the Issue of serializing Func<T>
            if (IsDelegate(propertyType) || IsReserved(propertyType))
            {
                property.ShouldSerialize = instance => false;
            }
            return property;
        }

        private string ConvertCamelCase(string str)
        {
            if (str == null)
            {
                return null;
            }

            if (str.Length == 0)
            {
                return str;
            }

            if (str.Length == 1)
            {
                return str.ToLower();
            }

            var leadWord = str.Substring(0, 1).ToLower();
            var tailWords = str.Substring(1);
            return $"{leadWord}{string.Join(string.Empty, tailWords)}";
        }

        public static bool IsDelegate(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.FullName == "System.Delegate")
            {
                return true;
            }
            return IsDelegate(type.BaseType);
        }

        public static bool IsReserved(Type type)
        {
            return type != null && type.FullName != null && _skipSerialization.Contains(type.FullName);
        }
    }
}
