// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal interface IBlobArgumentBindingProvider
    {
        IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access);
    }
}
