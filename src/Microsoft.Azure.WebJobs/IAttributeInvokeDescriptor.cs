// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Describes how an attribute is converted to and from an string-representation that can be both logged 
    /// and used to invoke this function instance later. 
    /// An attribute may implement this interface, or a default implementation may be inferred. 
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute on this binding.</typeparam>
    public interface IAttributeInvokeDescriptor<TAttribute>
    {
        /// <summary>
        /// Get a string representation of this resolved attribute which can be logged and fed to FromInvokeString.
        /// </summary>
        /// <returns></returns>
        string ToInvokeString();

        /// <summary>
        /// Hydrate a resolved attribute from an invoke string which can then be used to create a binding. 
        /// </summary>
        /// <param name="invokeString">String representation of this argument</param>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "string")]
        TAttribute FromInvokeString(string invokeString);
    }
}