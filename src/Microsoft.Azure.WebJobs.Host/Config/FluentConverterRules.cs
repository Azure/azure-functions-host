// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Expose fluent APIs for adding converters.  This can apply to either:    
    ///  1. globally for all attributes - in which case it's called from <see cref="ExtensionConfigContext"/> and TAttribute is System.Attribute.
    ///  2. a specific attribute - in which case it's called from <see cref="FluentBindingRule{TAttribute}"/> 
    /// </summary>
    /// <typeparam name="TAttribute">The attribute type this applies to. Literally <see cref="Attribute"/> if it applies to all attributes.</typeparam>
    /// <typeparam name="TThis">For fluent API, the type to return</typeparam>
    public abstract class FluentConverterRules<TAttribute, TThis> where TAttribute : Attribute
    {
        // Access the converter manager that we're adding rules to. 
        internal abstract IConverterManager Converters { get; }

        /// <summary>
        /// Add basic converter
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TDestination"></typeparam>
        /// <param name="func"></param>
        /// <returns></returns>
        public TThis AddConverter<TSource, TDestination>(Func<TSource, TDestination> func)
        {
            FuncConverter<TSource, TAttribute, TDestination> wrapper = (src, attr, context) => func(src);
            this.Converters.AddConverter(wrapper);
            return (TThis)(object)this;
        }

        /// <summary>
        /// Add converter that uses the control attribute. 
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TDestination"></typeparam>
        /// <param name="func"></param>
        /// <returns></returns>
        public TThis AddConverter<TSource, TDestination>(Func<TSource, TAttribute, TDestination> func)
        {
            FuncConverter<TSource, TAttribute, TDestination> wrapper = (src, attr, context) => func(src, attr);
            this.Converters.AddConverter(wrapper);
            return (TThis)(object)this;
        }

        /// <summary>
        /// Add a converter for the given Source to Destination conversion.
        /// The typeConverter type is instantiated with the type arguments and constructorArgs is passed. 
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <param name="typeConverter">A type with conversion methods. This can be generic and will get instantiated with the 
        /// appropriate type parameters. </param>
        /// <param name="constructorArgs">Constructor Arguments to pass to the constructor when instantiated. This can pass configuration and state.</param>
        public TThis AddConverter<TSource, TDestination>(            
            Type typeConverter,
            params object[] constructorArgs)
        {
            var patternMatcher = PatternMatcher.New(typeConverter, constructorArgs);
            return AddConverterBuilder<TSource, TDestination>(patternMatcher);
        }

        /// <summary>
        /// Add a converter for the given Source to Destination conversion.
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <param name="converterInstance">Instance of an object with convert methods on it.</param>
        public TThis AddConverter<TSource, TDestination>(
          IConverter<TSource, TDestination> converterInstance)
        {
            var patternMatcher = PatternMatcher.New(converterInstance);
            return AddConverterBuilder<TSource, TDestination>(patternMatcher);
        }

        /// <summary>
        /// Add a converter for the given Source to Destination conversion.
        /// </summary>
        /// <typeparam name="TSource">Source type.</typeparam>
        /// <typeparam name="TDestination">Destination type.</typeparam>
        /// <param name="converterInstance">Instance of an object with convert methods on it.</param>
        public TThis AddConverter<TSource, TDestination>(
          IAsyncConverter<TSource, TDestination> converterInstance)
        {
            var patternMatcher = PatternMatcher.New(converterInstance);
            return AddConverterBuilder<TSource, TDestination>(patternMatcher);
        }

        private TThis AddConverterBuilder<TSource, TDestination>(          
          PatternMatcher patternMatcher)
        {
             this.Converters.AddConverter<TSource, TDestination, TAttribute>((typeSource, typeDest) =>
            {
                var converter = patternMatcher.TryGetConverterFunc(typeSource, typeDest);
                return converter;
            });
            return (TThis)(object)this;
        }
    }
}
