// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    [AttributeUsage(AttributeTargets.Parameter)]
    [Binding]
    public sealed class ManualTriggerAttribute : Attribute
    {
    }
}
