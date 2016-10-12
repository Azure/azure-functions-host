// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// General service for converting between types for parameter bindings.  
    /// Parameter bindings call this to convert from user parameter types to underlying binding types. 
    /// </summary>
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
    }

    /// <summary>
    /// Convenience methods for <see cref="IConverterManager"/>
    /// </summary>
    public static class IConverterManagerExtensions
    {
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
    }
}