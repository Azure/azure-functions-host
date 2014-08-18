// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IBindingDataProvider
    {
        IReadOnlyDictionary<string, Type> Contract { get; }

        IReadOnlyDictionary<string, object> GetBindingData(object value);
    }
}
