// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs
{   
    // Concrete implementation of IConverterManager
    internal class ConverterManager : IConverterManager
    {
        // Map from <TSrc,TDest> to a converter function. 
        // (Type) --> FuncConverter<object, TAttribute, object>
        private readonly Dictionary<string, object> _funcsWithAttr = new Dictionary<string, object>();

        private readonly List<Entry> _openConverters = new List<Entry>();

        public static readonly IConverterManager Identity = new IdentityConverterManager();

        public ConverterManager()
        {
            this.AddConverter<byte[], string>(DefaultByteArrayToString);
        }

        private void AddOpenConverter<TAttribute>(
            OpenType source,
            OpenType dest,
          Func<Type, Type, Func<object, object>> converterBuilder)
          where TAttribute : Attribute
        {            
            var entry = new Entry
            {
                Source = source,
                Dest = dest,
                Attribute = typeof(TAttribute),
                Builder = converterBuilder
            };
            this._openConverters.Add(entry);
        }

        private Func<Type, Type, Func<object, object>> TryGetOpenConverter(Type typeSource, Type typeDest, Type typeAttribute)
        {
            foreach (var entry in _openConverters)
            {
                if (entry.Attribute == typeAttribute)
                {
                    if (entry.Source.IsMatch(typeSource) && entry.Dest.IsMatch(typeDest))
                    {
                        return entry.Builder;
                    }
                }
            }
            return null;
        }

        private static string DefaultByteArrayToString(byte[] bytes)
        {
            string str = Encoding.UTF8.GetString(bytes);
            return str;
        }

        private static string GetKey<TSrc, TDest, TAttribute>()
        {
            return typeof(TSrc).FullName + "|" + typeof(TDest).FullName + "|" + typeof(TAttribute).FullName;
        }

        internal static OpenType GetTypeValidator<T>()
        {
            return GetTypeValidator(typeof(T));            
        }

        internal static OpenType GetTypeValidator(Type type)
        {
            var openType = GetOpenType(type);
            if (openType != null)
            {
                return openType;
            }

            return new ExactMatch(type);
        }

        // Gets an OpenType from the given argument. 
        // Return null if it's a concrete type (ie, not an open type).  
        private static OpenType GetOpenType<T>()
        {
            var t = typeof(T);
            return GetOpenType(t);
        }

        private static OpenType GetOpenType(Type t)
        {
            if (t == typeof(OpenType) || t == typeof(object))
            {
                return new AnythingOpenType();
            }
            if (typeof(OpenType).IsAssignableFrom(t))
            {
                return (OpenType)Activator.CreateInstance(t);
            }

            if (t.IsArray)
            {
                var elementType = t.GetElementType();
                var innerType = GetTypeValidator(elementType);
                return new ArrayOpenType(innerType);
            }

            // Rewriter rule for generics so customers can say: IEnumerable<OpenType> 
            if (t.IsGenericType)
            {
                var outerType = t.GetGenericTypeDefinition();
                Type[] args = t.GetGenericArguments();
                if (args.Length == 1)
                {
                    var arg1 = GetOpenType(args[0]);
                    if (arg1 != null)
                    {
                        return new SingleGenericArgOpenType(outerType, arg1);
                    }
                    // This is a concrete generic type, like IEnumerable<JObject>. No open types needed. 
                }
                else
                {
                    // Just to sanity check, make sure there's no OpenType buried in the argument. 
                    foreach (var arg in args)
                    {
                        if (GetOpenType(arg) != null)
                        {
                            throw new NotSupportedException("Embedded Open Types are only supported for types with a single generic argument.");
                        }
                    }
                }
            }

            return null;
        }

        public void AddConverter<TSrc, TDest, TAttribute>(
            Func<Type, Type, Func<object, object>> converterBuilder)
            where TAttribute : Attribute
        {
            var openTypeSource = GetOpenType<TSrc>();
            var openTypeDest = GetOpenType<TDest>();

            if (openTypeSource == null)
            {
                openTypeSource = new ExactMatch(typeof(TSrc));
            }
            if (openTypeDest == null)
            {
                openTypeDest = new ExactMatch(typeof(TDest));
            }

            AddOpenConverter<TAttribute>(openTypeSource, openTypeDest, converterBuilder);
        }

        public void AddConverter<TSrc, TDest, TAttribute>(FuncConverter<TSrc, TAttribute, TDest> converter)
            where TAttribute : Attribute
        {
            string key = GetKey<TSrc, TDest, TAttribute>();
            _funcsWithAttr[key] = converter;                        
        }

        // Return null if not found. 
        private FuncConverter<TSrc, TAttribute, TDest> TryGetConverter<TSrc, TAttribute, TDest>()
            where TAttribute : Attribute
        {
            object obj;

            // First lookup specificially for TAttribute. 
            string keySpecific = GetKey<TSrc, TDest, TAttribute>();
            if (_funcsWithAttr.TryGetValue(keySpecific, out obj))
            {               
                var func = (FuncConverter<TSrc, TAttribute, TDest>)obj;
                return func;
            }

            // No specific case, lookup in the general purpose case. 
            string keyGeneral = GetKey<TSrc, TDest, Attribute>();
            if (_funcsWithAttr.TryGetValue(keyGeneral, out obj))
            {
                var func1 = (FuncConverter<TSrc, Attribute, TDest>)obj;
                FuncConverter<TSrc, TAttribute, TDest> func2 = (src, attr, context) => func1(src, null, context);
                return func2;
            }

            return null;
        }

        public FuncConverter<TSrc, TAttribute, TDest> GetConverter<TSrc, TDest, TAttribute>()
            where TAttribute : Attribute
        {
            // Give precedence to exact matches.
            // this lets callers override any other rules (like JSON binding) 

            // TSrc --> TDest
            var exactMatch = TryGetConverter<TSrc, TAttribute, TDest>();
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var typeSource = typeof(TSrc);
            var typeDest = typeof(TDest);

            // Inheritence (also covers idempotency)
            if (typeDest.IsAssignableFrom(typeSource))
            {
                // Skip implicit conversions to object since that's everybody's base 
                // class and BindToInput<attr,Object> would catch everything. 
                // Users can still register an explicit T-->object converter if they want to 
                // support it. 
                if (typeDest != typeof(Object))
                {
                    return (src, attr, context) =>
                    {
                        object obj = (object)src;
                        return (TDest)obj;
                    };
                }
            }

            // Object --> TDest
            // Catch all for any conversion to TDest
            var objConversion = TryGetConverter<object, TAttribute, TDest>();
            if (objConversion != null)
            {
                return (src, attr, context) =>
                {
                    var result = objConversion(src, attr, context);
                    return result;
                };
            }

            {
                var builder = TryGetOpenConverter(typeSource, typeDest, typeof(TAttribute));
                if (builder != null)
                {
                    var converter = builder(typeSource, typeDest);
                    return (src, attr, context) => (TDest)converter(src);
                }
            }

            // string --> TDest
            var fromString = TryGetConverter<string, TAttribute, TDest>();
            if (fromString == null)
            {
                return null;
            }

            // String --> TDest
            if (typeSource == typeof(string))
            {
                return (src, attr, context) =>
                {
                    var result = fromString((string)(object)src, attr, context);
                    return result;
                };
            }

            // Allow some well-defined intermediate conversions. 
            // If this is "wrong" for your type, then it should provide an exact match to override.

            // Byte[] --[builtin]--> String --> TDest
            if (typeSource == typeof(byte[]))
            {
                var bytes2string = TryGetConverter<byte[], TAttribute, string>();

                return (src, attr, context) =>
                {
                    byte[] bytes = (byte[])(object)src;
                    string str = bytes2string(bytes, attr, context);
                    var result = fromString(str, attr, context);
                    return result;
                };
            }

            // General JSON serialization rule. 

            if (typeSource.IsPrimitive ||
               (typeSource == typeof(object)) ||
                typeof(IEnumerable).IsAssignableFrom(typeSource))
            {
                return null;
            }

            var funcJobj = TryGetConverter<object, TAttribute, JObject>();
            if (funcJobj == null)
            {
                funcJobj = (object obj, TAttribute attr, ValueBindingContext context) => JObject.FromObject(obj);
            }

            // TSrc --[Json]--> string --> TDest
            return (src, attr, context) =>
            {
                JObject jobj = funcJobj((object)src, attr, context);
                string json = jobj.ToString();
                TDest obj = fromString(json, attr, context);
                return obj;
            };
        }

        // List of open converters. Since these are not exact type matches, need to search through and determine the match. 
        private class Entry
        {
            public OpenType Source { get; set; }
            public OpenType Dest { get; set; }
            public Type Attribute { get; set; }

            public Func<Type, Type, Func<object, object>> Builder { get; set; }
        }

        // "Empty" converter manager that only allows identity conversions. 
        // This is useful for constrained rules that don't want to operate against exact types and skip 
        // arbitrary user conversions. 
        private class IdentityConverterManager : IConverterManager
        {
            public void AddConverter<TSource, TDestination, TAttribute1>(FuncConverter<TSource, TAttribute1, TDestination> converter) where TAttribute1 : Attribute
            {
                throw new NotImplementedException();
            }

            public void AddConverter<TSrc, TDest, TAttribute1>(Func<Type, Type, Func<object, object>> converterBuilder) where TAttribute1 : Attribute
            {
                throw new NotImplementedException();
            }

            public FuncConverter<TSource, TAttribute1, TDestination> GetConverter<TSource, TDestination, TAttribute1>() where TAttribute1 : Attribute
            {
                if (typeof(TSource) != typeof(TDestination))
                {
                    return null;
                }
                return (src, attr, ctx) =>
                {
                    object obj = (object)src;
                    return (TDestination)obj;
                };
            }
        }

        // Match a generic type with 1 generic arg. 
        // like IEnumerable<T>,  IQueryable<T>, etc. 
        private class SingleGenericArgOpenType : OpenType
        {
            private readonly OpenType _inner;
            private readonly Type _outerType;

            public SingleGenericArgOpenType(Type outerType, OpenType inner)
            {
                _inner = inner;
                _outerType = outerType;
            }

            public override bool IsMatch(Type type)
            {
                if (type == null)
                {
                    throw new ArgumentNullException("type");
                }
                if (type.IsGenericType &&
                    type.GetGenericTypeDefinition() == _outerType)
                {
                    var args = type.GetGenericArguments();

                    return _inner.IsMatch(args[0]);
                }

                return false;
            }
        }

        // Bind to any type
        private class AnythingOpenType : OpenType
        {
            public override bool IsMatch(Type type)
            {
                return true;
            }
        }
                
        private class ExactMatch : OpenType
        {
            private readonly Type _type;
            public ExactMatch(Type type)
            {
                _type = type;
            }
            public override bool IsMatch(Type type)
            {
                return type == _type;
            }
        }

        // Matches any T[] 
        private class ArrayOpenType : OpenType
        {
            private readonly OpenType _inner;
            public ArrayOpenType(OpenType inner)
            {
                _inner = inner;
            }
            public override bool IsMatch(Type type)
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                if (type.IsArray)
                {
                    var elementType = type.GetElementType();
                    return _inner.IsMatch(elementType);
                }
                return false;
            }
        }
    } // end class ConverterManager
}