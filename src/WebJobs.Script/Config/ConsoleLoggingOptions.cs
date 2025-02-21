// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ConsoleLoggingOptions
    {
        // A typical out-of-proc function execution will generate 8 log lines.
        // A single core container can potentially get around 1K RPS at the higher end, and a typical log line is around 300 bytes
        // So in the extreme case, this is about 1 second of buffer and should be less than 3MB
        public const int DefaultBufferSize = 8000;

        public bool LoggingDisabled { get; set; }

        // Use BufferSize = 0 to disable the buffer
        public bool BufferEnabled { get; set; } = true;

        public int BufferSize { get; set; } = DefaultBufferSize;

        /// <summary>
        /// Gets or sets the <see cref="TextWriter"/> to write logs to.
        /// IMPORTANT: this is primarily for unit tests to redirect logs to a different writer.
        /// </summary>
        internal TextWriter Writer { get; set; } = Console.Out;
    }
}
