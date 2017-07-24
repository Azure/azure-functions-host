﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal enum ChannelState
    {
        Stopped,
        Started,
        Connected,
        Faulted
    }

    // wrapper around proc.start & grpc channel with some state management
    internal interface ILanguageWorkerChannel : IDisposable
    {
        // TODO: use state machine framework like stateless?
        ChannelState State { get; set; }

        Task StartAsync();

        Task StopAsync();

        Task HandleFileEventAsync(FileSystemEventArgs fileEvent);

        void LoadAsync(FunctionMetadata functionMetadata);

        Task<object> InvokeAsync(FunctionMetadata functionMetadata, Dictionary<string, object> scriptExecutionContext);
    }
}
