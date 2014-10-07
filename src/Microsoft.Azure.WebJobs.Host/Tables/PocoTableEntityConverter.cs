// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class PocoTableEntityConverter
    {
        // Beware, we deserializing, DateTimes may arbitrarily be Local or UTC time.
        // Callers can normalize via DateTime.ToUniversalTime()
        // Can't really normalize here because DateTimes could be embedded deep in the target type.
        private static object BindFromString(string input, Type target)
        {
            MethodInfo method = typeof(PocoTableEntityConverter).GetMethod(
                "BindFromStringGeneric", BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(method != null);
            MethodInfo genericMethod = method.MakeGenericMethod(target);
            Debug.Assert(genericMethod != null);
            Func<string, object> lambda =
                (Func<string, object>)Delegate.CreateDelegate(typeof(Func<string, object>), genericMethod);
            return lambda.Invoke(input);
        }

        internal static object BindFromStringGeneric<TOutput>(string input)
        {
            IConverter<string, TOutput> converter = StringToTConverterFactory.Instance.TryCreate<TOutput>();

            // It's possible we end up here if the string was JSON and we should have been using a JSON deserializer instead. 
            if (converter == null)
            {
                string msg = string.Format("Can't bind from string to type '{0}'", typeof(TOutput).FullName);
                throw new InvalidOperationException(msg);
            }

            return converter.Convert(input);
        }

        // Dictionary is a copy (immune if source object gets mutated)        
        public static IDictionary<string, string> ConvertObjectToDict(object obj)
        {
            if (obj == null)
            {
                return new Dictionary<string, string>();
            }

            if (obj is IDictionary<string, string>)
            {
                // Per contract above, clone.
                return new Dictionary<string, string>((IDictionary<string, string>)obj);
            }

            Dictionary<string, string> d = new Dictionary<string, string>();
            Type objectType = obj.GetType();

            foreach (var prop in objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = prop.GetValue(obj, null);
                if (value != null)
                {
                    string result;
                    Type type = prop.PropertyType;
                    result = SerializeObject(value, type);
                    d[prop.Name] = result;
                }
            }
            return d;
        }

        private static string SerializeObject(object value, Type type)
        {
            string result;
            if (type == typeof(DateTimeOffset?) ||
                type == typeof(DateTimeOffset))
            {
                result = SerializeDateTimeOffset((DateTimeOffset)value);
            }
            else if (type == typeof(DateTime?) ||
                type == typeof(DateTime))
            {
                result = SerializeDateTime((DateTime)value);
            }
            else if (UseToStringParser(type))
            {
                result = value.ToString();
            }
            else
            {
                result = (value == null) ? String.Empty :
                    JsonConvert.SerializeObject(value, JsonSerialization.Settings);
            }
            return result;
        }

        public static T ConvertDictToObject<T>(IDictionary<string, string> data) where T : new()
        {
            if (data == null)
            {
                return default(T);
            }

            // casting to object to allow boxing of value types.
            // boxing is required to make SetValue below update original object (and not a copy of it)
            object obj = new T();
            foreach (var kv in data)
            {
                var prop = typeof(T).GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    object value;
                    string str = kv.Value;
                    Type type = prop.PropertyType;
                    value = DeserializeObject(str, type);
                    prop.SetValue(obj, value, null);
                }
            }

            return (T)obj;
        }

        private static object DeserializeObject(string str, Type type)
        {
            object value;
            if (type == typeof(DateTimeOffset))
            {
                value = DeserializeDateTimeOffset(str);
            }
            else if (type == typeof(DateTimeOffset?))
            {
                DateTimeOffset? v2 = DeserializeDateTimeOffset(str);
                value = v2;
            }
            else if (type == typeof(DateTime))
            {
                value = DeserializeDateTime(str);
            }
            else if (type == typeof(DateTime?))
            {
                DateTime? v2 = DeserializeDateTime(str);
                value = v2;
            }
            else if (UseToStringParser(type))
            {
                value = BindFromString(str, type);
            }
            else
            {
                value = JsonConvert.DeserializeObject(str, type, JsonSerialization.Settings);
            }
            return value;
        }

        // DateTime.ToString() is not specific enough, so need better serialization functions.
        private static string SerializeDateTime(DateTime date)
        {
            // DateTime is tricky. It doesn't include TimeZone, but does include 
            // a DateTime.Kind property which controls the "view" exposed via ToString. 
            // So:
            //   x1.ToString() == x2.ToString(), even though:
            //   x1.ToUniversalTime().ToString() != x2.ToUniversalTime().ToString()

            // Write out a very specific format (UTC with timezone) so that our parse
            // function can read properly. 

            // Write out dates using ISO 8601 format.
            // http://msdn.microsoft.com/en-us/library/az4se3k1.aspx
            // This is the same format JSON.Net will use, although it will write as a string
            // so the result is embedded in quotes. 
            return date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static string SerializeDateTimeOffset(DateTimeOffset date)
        {
            return date.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime DeserializeDateTime(string s)
        {
            // Parse will read in a variety of formats (even ones not written without timezone info)
            var x = DateTime.Parse(s, CultureInfo.InvariantCulture);

            if (x.Kind == DateTimeKind.Unspecified)
            {
                // Assume if there's no timezone info, it's UTC.
                return DateTime.SpecifyKind(x, DateTimeKind.Utc);
            }
            return x.ToUniversalTime();
        }

        private static DateTimeOffset DeserializeDateTimeOffset(string s)
        {
            return DateTimeOffset.ParseExact(s, "o", CultureInfo.InvariantCulture);
        }

        // We have 3 parsing formats:
        // - DateTime (see SerializeDateTime)
        // - ToString / TryParse
        // - JSON 
        // Make sure serialization/Deserialization agree on the types.
        // Parses are *not* compatible, especially for same types. 
        private static bool UseToStringParser(Type t)
        {
            // JOSN requires strings to be quoted. 
            // The practical effect of adding some of these types just means that the values don't need to be quoted. 
            // That gives them higher compatibily with just regular strings. 
            return IsDefaultTableType(t) ||
                (t == typeof(char)) ||
                (t.IsEnum) || // ensures Enums are represented as string values instead of numerical.
                (t == typeof(TimeSpan)
                );
        }

        // Is this a type that is already serialized by default?
        // See list of types here: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        private static bool IsDefaultTableType(Type t)
        {
            if ((t == typeof(byte[])) ||
                (t == typeof(bool)) ||
                (t == typeof(DateTime)) ||
                (t == typeof(double)) ||
                (t == typeof(Guid)) ||
                (t == typeof(Int32)) ||
                (t == typeof(Int64)) ||
                (t == typeof(string))
                )
            {
                return true;
            }

            // Nullables are written too. 
            if (t.IsGenericType)
            {
                var tOpen = t.GetGenericTypeDefinition();
                if (tOpen == typeof(Nullable<>))
                {
                    var tArg = t.GetGenericArguments()[0];
                    return IsDefaultTableType(tArg);
                }
            }

            return false;
        }
    }
}
