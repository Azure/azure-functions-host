// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionInstanceLogger
    {
        string LogFunctionStarted(FunctionStartedMessage message);

        void LogFunctionCompleted(FunctionCompletedMessage message);

        void DeleteLogFunctionStarted(string startedMessageId);
    }
}
