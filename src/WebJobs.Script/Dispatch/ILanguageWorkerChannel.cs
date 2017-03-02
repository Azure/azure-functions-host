// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal enum ChannelState
    {
        Stopped,
        Started
    }

    // wrapper around proc.start & grpc channel with some state management
    internal interface ILanguageWorkerChannel
    {
        ChannelState State { get; set; }

        Task Start();

        Task Load(FunctionMetadata functionMetadata);

        Task<object> Invoke(object[] parameters);
    }
}
