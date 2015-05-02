// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Interface defining methods used to create binding data.
    /// </summary>
    public interface IBindingDataProvider
    {
        /// <summary>
        /// Gets the binding contract.
        /// </summary>
        IReadOnlyDictionary<string, Type> Contract { get; }

        /// <summary>
        /// Gets the binding data.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        IReadOnlyDictionary<string, object> GetBindingData(object value);
    }
}
