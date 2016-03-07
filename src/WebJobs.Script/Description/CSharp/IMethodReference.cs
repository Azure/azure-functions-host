// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface IMethodReference
    {
        string Name { get; }

        bool IsPublic { get; }
    }
}