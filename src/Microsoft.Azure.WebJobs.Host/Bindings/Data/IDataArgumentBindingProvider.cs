// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Data
{
    internal interface IDataArgumentBindingProvider<TBindingData>
    {
        IArgumentBinding<TBindingData> TryCreate(ParameterInfo parameter);
    }
}
