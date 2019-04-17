// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class FunctionIndexingEvent : ScriptEvent
    {
        public FunctionIndexingEvent(string name, string source, Exception exception)
            : base(name, source)
        {
            Exception = exception;
        }

        public Exception Exception { get; private set; }
    }
}