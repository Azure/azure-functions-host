// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Entry for a recent function 
    /// </summary>
    public interface IRecentFunctionEntry : IFunctionInstanceBaseEntry
    {
        /// <summary>
        /// Container name this interval is for. If multiple instances are running, each has a different container name. 
        /// </summary>
        string ContainerName { get; }

        /// <summary>
        /// Display name summary. 
        /// This can include per-instance information like parameters to aide in picking out the right instance in from a collection of many invocations.
        /// For example: "Foo(1,2,3)"
        /// </summary>
        string DisplayName { get; }     
    }

}