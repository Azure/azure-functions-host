// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal interface IFunctionOutput : IDisposable
    {
        IRecurrentCommand UpdateCommand { get; }

        TextWriter Output { get; }

        Task SaveAndCloseAsync(CancellationToken cancellationToken);
    }
}
