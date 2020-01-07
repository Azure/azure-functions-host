// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Attribute applied to actions to indicate whether the resource returned by an action
    /// contains secrets.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ResourceContainsSecretsAttribute : Attribute
    {
    }
}
