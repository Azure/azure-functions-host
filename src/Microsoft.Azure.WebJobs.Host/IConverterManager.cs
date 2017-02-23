// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// General service for converting between types for parameter bindings.  
    /// Parameter bindings call this to convert from user parameter types to underlying binding types. 
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    public interface IConverterManager
    {
        /// <summary>
        /// Get a conversion function to converter from the source to the destination type. 
        /// This will either return a converter directly supplied by AddConverter or a composition of converters:
        /// 1. Exact Match: If there is a TSource-->TDestination converter, return that. 
        /// 2. Catch-all: If there is an object-->TDestination converter, return that. 
        /// 3. Inheritance : if TSource is assignable to TDestination (such as inheritance or if the types are the same), do the automatic conversion. 
        /// 4. byte[]:  if there is a Byte[] --> string, and String--> TDestination, compose them to do Byte[]-->String
        /// 5. Poco with Json: if TSource is a poco, serialize it to a string and use string-->TDestination. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <returns>a converter function; or null if no converter is available.</returns>
        FuncConverter<TSource, TAttribute, TDestination> GetConverter<TSource, TDestination, TAttribute>()
            where TAttribute : Attribute;

        /// <summary>
        /// Add a converter function which can then be retrieved by GetConverter. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <param name="converter">the converter function for this combination of type parameters.</param>
        void AddConverter<TSource, TDestination, TAttribute>(FuncConverter<TSource, TAttribute, TDestination> converter)
            where TAttribute : Attribute;

        /// <summary>
        /// Add a builder function that returns a converter. This can use <see cref="Microsoft.Azure.WebJobs.Host.Bindings.OpenType"/>  to match against an 
        /// open set of types. The builder can then do one time static type checking and code gen caching before
        /// returning a converter function that is called on each invocation. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <param name="converterBuilder">A function that is invoked if-and-only-if there is a compatible type match for the 
        /// source and destination types. It then produce a converter function that can be called many times </param>
        void AddConverter<TSource, TDestination, TAttribute>(
          Func<Type, Type, Func<object, object>> converterBuilder)
          where TAttribute : Attribute;
    }

    /// <summary>
    /// Convenience methods for <see cref="IConverterManager"/>
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    public static class IConverterManagerExtensions
    {
        private static readonly MethodInfo ConverterMethod = typeof(IConverterManagerExtensions).GetMethod("HasConverterWorker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Register a new converter function that applies for all attributes. 
        /// If TSource is object, then this converter is applied to any attempt to convert to TDestination. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <param name="converterManager"></param>
        /// <param name="converter">A function to convert from the source to the destination type.</param>
        public static void AddConverter<TSource, TDestination>(this IConverterManager converterManager, Func<TSource, TDestination> converter)
        {
            FuncConverter<TSource, Attribute, TDestination> func = (src, attr, context) => converter(src);
            converterManager.AddConverter(func);
        }

        /// <summary>
        /// Register a new converter function that is influenced by the attribute. 
        /// If TSource is object, then this converter is applied to any attempt to convert to TDestination. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <param name="converterManager"></param>
        /// <param name="converter">A function to convert from the source to the destination type.</param>
        public static void AddConverter<TSource, TDestination, TAttribute>(this IConverterManager converterManager, Func<TSource, TAttribute, TDestination> converter)
            where TAttribute : Attribute
        {
            FuncConverter<TSource, TAttribute, TDestination> func = (src, attr, context) => converter(src, attr);
            converterManager.AddConverter(func);
        }

        /// <summary>
        /// Add a converter for the given Source to Destination conversion.
        /// The typeConverter type is instantiated with the type arguments and constructorArgs is passed. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <param name="converterManager">Instance of Converter Manager.</param>
        /// <param name="typeConverter">A type with conversion methods. This can be generic and will get instantiated with the 
        /// appropriate type parameters. </param>
        /// <param name="constructorArgs">Constructor Arguments to pass to the constructor when instantiated. This can pass configuration and state.</param>
        public static void AddConverter<TSource, TDestination, TAttribute>(
            this IConverterManager converterManager,
            Type typeConverter,
            params object[] constructorArgs)
            where TAttribute : Attribute
        {
            var patternMatcher = PatternMatcher.New(typeConverter, constructorArgs);
            converterManager.AddConverterBuilder<TSource, TDestination, TAttribute>(patternMatcher);
        }

        /// <summary>
        /// Add a converter for the given Source to Destination conversion.
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <typeparam name="TAttribute">Attribute on the binding. </typeparam>
        /// <param name="converterManager">Instance of Converter Manager.</param>
        /// <param name="converterInstance">Instance of an object with convert methods on it.</param>
        public static void AddConverter<TSource, TDestination, TAttribute>(
          this IConverterManager converterManager,
          object converterInstance)
          where TAttribute : Attribute
        {
            var patternMatcher = PatternMatcher.New(converterInstance);
            converterManager.AddConverterBuilder<TSource, TDestination, TAttribute>(patternMatcher);
        }

        private static void AddConverterBuilder<TSource, TDestination, TAttribute>(
          this IConverterManager converterManager,
          PatternMatcher patternMatcher)
          where TAttribute : Attribute
        {
            if (converterManager == null)
            {
                throw new ArgumentNullException("converterManager");
            }

            converterManager.AddConverter<TSource, TDestination, TAttribute>((typeSource, typeDest) =>
            {
                var converter = patternMatcher.TryGetConverterFunc(typeSource, typeDest);
                return converter;
            });
        }

        private static bool HasConverterWorker<TAttribute, TSrc, TDest>(IConverterManager converterManager)
               where TAttribute : Attribute
        {
            var func = converterManager.GetConverter<TSrc, TDest, TAttribute>();
            return func != null;
        }

        // Provide late-bound access to check if a conversion exists. 
        internal static bool HasConverter<TAttribute>(this IConverterManager converterManager, Type typeSource, Type typeDest)
            where TAttribute : Attribute
        {
            var method = ConverterMethod.MakeGenericMethod(typeof(TAttribute), typeSource, typeDest);
            var result = method.Invoke(null, new object[] { converterManager });
            return (bool)result;
        }
    }
}