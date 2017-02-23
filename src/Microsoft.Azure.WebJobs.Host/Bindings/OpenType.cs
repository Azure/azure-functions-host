// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Placeholder to use with converter manager for describing generic types.     
    /// Derived classes can override IsMatch to provide a constraint. 
    ///  OpenType matches any type. 
    ///  MyDerivedType matches any type where IsMatch(type) is true. 
    /// Also applies to generics such as: 
    ///  GenericClass&lt;OpenType&gt; 
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    public abstract class OpenType
    {
        /// <summary>
        /// Return true if and only if given type matches. 
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns></returns>
        public abstract bool IsMatch(Type type);
    }
}
