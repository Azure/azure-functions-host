// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionOutput : IDisposable
    {
        ICanFailCommand UpdateCommand { get; }

        TextWriter Output { get; }

        void SaveAndClose();
    }
}
