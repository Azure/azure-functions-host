// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal interface ITriggerData
    {
        IValueProvider ValueProvider { get; }

        IReadOnlyDictionary<string, object> BindingData { get; }
    }
}
