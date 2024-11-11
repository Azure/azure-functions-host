// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// A logger factory which is used to create loggers for system-only logs.
    /// </summary>
    internal interface ISystemLoggerFactory : ILoggerFactory
    {
    }
}
