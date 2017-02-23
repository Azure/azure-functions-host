// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Helper Extension methods for extension configuration. 
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    public static class ExtensionConfigContextExtensions
    {
        /// <summary>
        /// Register a set of rules to handle binding to a given attribute. 
        /// </summary>
        /// <typeparam name="TAttribute">The attribute to bind to</typeparam>
        /// <param name="context"></param>
        /// <param name="validator">a validator function to invoke on the attribute before runtime. </param>
        /// <param name="bindingProviders">list of binding providers to handle this attribute. 
        /// If none of these handle the attribute, an error is thrown at indexing. </param>
        public static void RegisterBindingRules<TAttribute>(
            this ExtensionConfigContext context,
            Action<TAttribute, Type> validator,
            params IBindingProvider[] bindingProviders)
            where TAttribute : Attribute
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            if (validator == null)
            {
                throw new ArgumentNullException("validator");
            }
            if (bindingProviders == null)
            {
                throw new ArgumentNullException("bindingProviders");
            }
            
            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();
            INameResolver nameResolver = context.Config.NameResolver;
            extensions.RegisterBindingRules<TAttribute>(validator, nameResolver, bindingProviders);
        }

        /// <summary>
        /// Register a set of rules to handle binding to a given attribute. 
        /// </summary>
        /// <typeparam name="TAttribute">The attribute to bind to</typeparam>
        /// <param name="context"></param>
        /// <param name="bindingProviders">list of binding providers to handle this attribute. 
        /// If none of these handle the attribute, an error is thrown at indexing. </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static void RegisterBindingRules<TAttribute>(
            this ExtensionConfigContext context,
            params IBindingProvider[] bindingProviders)
            where TAttribute : Attribute
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }        
            if (bindingProviders == null)
            {
                throw new ArgumentNullException("bindingProviders");
            }

            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();
            extensions.RegisterBindingRules<TAttribute>(bindingProviders);
        }
    }
}
