// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal interface IBlobArgumentBindingProvider
    {
        IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access);
    }
}
