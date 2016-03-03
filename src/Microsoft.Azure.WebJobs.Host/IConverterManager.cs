// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// General service for converting between types. 
    /// Parameter bindings call this to convert from user parameter types to underlying binding types. 
    /// </summary>
    public interface IConverterManager
    {
        /// <summary>
        /// Get a conversion function to converter from the source to the destination type. 
        /// This will either return a converter directly supplied by <see cref="AddConverter{TSource, TDestination}(Func{TSource, TDestination})"/> or a composition of converters:
        /// 1. Exact Match: If there is a TSource-->TDestination converter, return that. 
        /// 2. Catch-all: If there is an object-->TDestination converter, return that. 
        /// 3. Inheritance : if TSource is assignable to TDestination (such as inheritance or if the types are the same), do the automatic conversion. 
        /// 4. byte[]:  if there is a Byte[] --> string, and String--> TDestination, compose them to do Byte[]-->String
        /// 5. Poco with Json: if TSource is a poco, serialize it to a string and use string-->TDestination. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <returns></returns>
        Func<TSource, TDestination> GetConverter<TSource, TDestination>();

        /// <summary>
        /// Register a new converter function. 
        /// If TSource is object, then this converter is applied to any attempt to convert to TDestination. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <param name="converter">A function to convert from the source to the destination type.</param>
        void AddConverter<TSource, TDestination>(Func<TSource, TDestination> converter);
    }
}