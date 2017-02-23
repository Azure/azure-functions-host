// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Converter function to use with <see cref="IConverterManager"/>
    /// </summary>
    /// <typeparam name="TSource">source to convert from</typeparam>
    /// <typeparam name="TAttribute">attribute on the binding. This may have parameter that influence conversion.</typeparam>
    /// <typeparam name="TDestination">destination type to convert to. </typeparam>
    /// <param name="src">source</param>
    /// <param name="attribute">attribute</param>
    /// <param name="context">binding context that may have additional parameters to influence conversion. </param>
    [Obsolete("Not ready for public consumption.")]
    public delegate TDestination FuncConverter<TSource, TAttribute, TDestination>(TSource src, TAttribute attribute, ValueBindingContext context)
            where TAttribute : Attribute;
}