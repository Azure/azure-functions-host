// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// For Attributes that implement <see cref="IConnectionProvider"/> this attribute can be
    /// applied to the Attribute to specify the attribute Type used for hierarchical
    /// declarative overrides.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ConnectionProviderAttribute : Attribute
    {
        /// <summary>
        /// Creates a new instance using the specified provider type.
        /// </summary>
        /// <param name="providerType">The Type of the provider.</param>
        public ConnectionProviderAttribute(Type providerType)
        {
            ProviderType = providerType;
        }

        /// <summary>
        /// Gets the type of the override provider.
        /// </summary>
        public Type ProviderType { get; }
    }
}
