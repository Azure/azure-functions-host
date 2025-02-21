// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="ISystemLoggerFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">The logger factory from the root container to wrap.</param>
    internal class SystemLoggerFactory(ILoggerFactory loggerFactory) : ISystemLoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
            => throw new InvalidOperationException("Cannot add providers to the system logger factory.");

        public ILogger CreateLogger(string categoryName) => loggerFactory.CreateLogger(categoryName);

        public void Dispose()
        {
            // No op - we do not dispose the provided logger factory.
        }
    }
}
