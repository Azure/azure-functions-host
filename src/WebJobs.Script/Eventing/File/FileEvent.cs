// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public sealed class FileEvent : ScriptEvent
    {
        public FileEvent(string source, FileSystemEventArgs args)
            : base(nameof(FileEvent), source)
        {
            FileChangeArguments = args;
        }

        public FileSystemEventArgs FileChangeArguments { get; }
    }
}
