// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    /// <summary>
    /// Defines an interface for providing binding extensions for Table bindings.
    /// Extensions can be registered using the <see cref="IExtensionRegistry"/> service
    /// from the <see cref="JobHostConfiguration"/>.
    /// </summary>
    internal interface ITableArgumentBindingExtensionProvider
    {
        /// <summary>
        /// Attempts to create a <see cref="ITableArgumentBindingExtension"/> for the specified parameter type.
        /// </summary>
        /// <param name="parameter">The parameter to attempt to bind to.</param>
        /// <returns>A <see cref="ITableArgumentBindingExtension"/> if the bind was successful, otherwise null.</returns>
        ITableArgumentBindingExtension TryCreate(ParameterInfo parameter);
    }
}
