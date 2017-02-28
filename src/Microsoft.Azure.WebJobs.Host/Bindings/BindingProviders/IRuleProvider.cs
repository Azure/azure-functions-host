// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Enable binding rules (like <see cref="IBindingProvider"/> to be self-describing for tooling. 
    /// </summary>
    internal interface IRuleProvider
    {
        /// <summary>
        /// Get individual rules this binding provides. 
        /// </summary>
        /// <returns></returns>
        IEnumerable<Rule> GetRules();

        /// <summary>
        /// Determine a "default type" for the given binding combination. 
        /// </summary>
        /// <param name="attribute">attribute for binding. This does not need to be resolved. </param>
        /// <param name="access">direction of the binding. Input, output, or in/out. </param>
        /// <param name="requestedType">object if none requested. Else a specific type (string, byte[], stream, JObject) that we should try to bind.
        /// </param>
        /// <returns>Null if unknown. Else a type that the parameter can be bound to. </returns>
        Type GetDefaultType(
          Attribute attribute,
          FileAccess access, 
          Type requestedType);
    }
}