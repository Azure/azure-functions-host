// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class ObjectBinderHelpers
    {
        public static bool CanBindFromString(Type targetType)
        {
            if (targetType == typeof(string))
            {
                return true;
            }

            MethodInfo tryParseMethod = targetType.GetMethod("TryParse", new[] { typeof(string), targetType.MakeByRefType() });
            if (tryParseMethod != null)
            {
                return true;
            }

            var converter = GetConverter(targetType);
            if (converter != null)
            {
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return true;

                }
            }

            if (targetType.IsEnum)
            {
                return true;
            }

            return false;
        }

        // Beware, we deserializing, DateTimes may arbitrarily be Local or UTC time.
        // Callers can normalize via DateTime.ToUniversalTime()
        // Can't really normalize here because DateTimes could be embedded deep in the target type.
        public static object BindFromString(string input, Type target)
        {
            if (target == typeof(string))
            {
                return input;
            }

            // Invoke:  success = Target.TryParse(input, out value)
            MethodInfo tryParseMethod = target.GetMethod("TryParse", new[] { typeof(string), target.MakeByRefType() });
            if (tryParseMethod != null)
            {
                object[] args = new object[] { input, null };
                bool success = (bool)tryParseMethod.Invoke(null, args);
                if (!success)
                {
                    string msg = string.Format("Parameter is illegal format to parse as type '{0}'", target.FullName);
                    throw new InvalidOperationException(msg);
                }
                return args[1];
            }

            // Look for a type converter. 
            // Do this before Enums to give it higher precedence. 
            var converter = GetConverter(target);
            if (converter != null)
            {
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFrom(input);
                }
            }

            // Enum support 
            if (target.IsEnum)
            {
                return Enum.Parse(target, input, ignoreCase: true);
            }

            // It's possible we end up here if the string was JSON and we should have been using a JSON deserializer instead. 
            {
                string msg = string.Format("Can't bind from string to type '{0}'", target.FullName);
                throw new InvalidOperationException(msg);
            }
        }

        // BCL implementation may get wrong converters
        // It appears to use Type.GetType() to find a converter, and so has trouble looking up converters from different loader contexts.
        static TypeConverter GetConverter(Type type)
        {
            // $$$ There has got to be a better way than this to make TypeConverters work.
            foreach (TypeConverterAttribute attr in type.GetCustomAttributes(typeof(TypeConverterAttribute), false))
            {
                string assemblyQualifiedName = attr.ConverterTypeName;
                if (!string.IsNullOrWhiteSpace(assemblyQualifiedName))
                {
                    // Type.GetType() may fail due to loader context issues.
                    string assemblyName = type.Assembly.FullName;

                    if (assemblyQualifiedName.EndsWith(assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        int i = assemblyQualifiedName.IndexOf(',');
                        if (i > 0)
                        {
                            string typename = assemblyQualifiedName.Substring(0, i);

                            var a = type.Assembly;
                            var t2 = a.GetType(typename); // lookup type name relative to the 
                            if (t2 != null)
                            {
                                var instance = Activator.CreateInstance(t2);
                                return (TypeConverter)instance;
                            }
                        }
                    }
                }
            }

            return TypeDescriptor.GetConverter(type);
        }

        private static IDictionary<string, string> ConvertDict<TValue>(IDictionary<string, TValue> source)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (var kv in source)
            {
                d[kv.Key] = kv.Value.ToString();
            }
            return d;
        }

        static MethodInfo methodConvertDict = typeof(ObjectBinderHelpers).GetMethod("ConvertDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        // Dictionary is a copy (immune if source object gets mutated)        
        public static IDictionary<string, string> ConvertObjectToDict(object obj)
        {
            if (obj == null)
            {
                return new Dictionary<string, string>();
            }
            Type objectType = obj.GetType();

            // Does type implemnet IDictionary<string, TValue>?
            // If so, run through and call
            foreach (var typeInterface in objectType.GetInterfaces())
            {
                if (typeInterface.IsGenericType)
                {
                    if (typeInterface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                    {
                        var typeArgs = typeInterface.GetGenericArguments();
                        if (typeArgs[0] == typeof(string))
                        {
                            var m = methodConvertDict.MakeGenericMethod(typeArgs[1]);
                            IDictionary<string, string> result = (IDictionary<string, string>)m.Invoke(null, new object[] { obj });
                            return result;
                        }
                    }
                }
            }

            Dictionary<string, string> d = new Dictionary<string, string>();

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

        public static string SerializeObject(object value, Type type)
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
                result = JsonCustom.SerializeObject(value, type);
            }
            return result;
        }

        public static T ConvertDictToObject<T>(IDictionary<string, string> data) where T : new()
        {
            if (data == null)
            {
                return default(T);
            }

            var obj = new T();
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

            return obj;
        }

        public static object DeserializeObject(string str, Type type)
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
                value = JsonCustom.DeserializeObject(str, type);
            }
            return value;
        }

        // DateTime.ToString() is not specific enough, so need better serialization functions.
        public static string SerializeDateTime(DateTime date)
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

        public static string SerializeDateTimeOffset(DateTimeOffset date)
        {
            return date.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        public static DateTime DeserializeDateTime(string s)
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

        public static DateTimeOffset DeserializeDateTimeOffset(string s)
        {
            return DateTimeOffset.ParseExact(s, "o", CultureInfo.InvariantCulture);
        }

        // We have 3 parsing formats:
        // - DateTime (see SerializeDateTime)
        // - ToString / TryParse
        // - JSON 
        // Make sure serialization/Deserialization agree on the types.
        // Parses are *not* compatible, especially for same types. 
        public static bool UseToStringParser(Type t)
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
