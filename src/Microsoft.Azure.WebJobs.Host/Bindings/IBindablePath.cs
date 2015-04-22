// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal interface IBindablePath<TPath>
    {
        bool IsBound { get; }

        IEnumerable<string> ParameterNames { get; }

        TPath Bind(IReadOnlyDictionary<string, object> bindingData);

        string ToString();
    }
}
