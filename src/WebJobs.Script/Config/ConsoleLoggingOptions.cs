// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ConsoleLoggingOptions
    {
        // A typical out-of-proc function execution will generate 8 log lines.
        // A single core container can potentially get around 1K RPS at the higher end, and a typical log line is around 300 bytes
        // So in the extreme case, this is about 1 second of buffer and should be less than 3MB
        public const int DefaultBufferSize = 8000;

        public ConsoleLoggingOptions()
        {
            LoggingDisabled = false;
            BufferEnabled = true;
            BufferSize = DefaultBufferSize;
        }

        public bool LoggingDisabled { get; set; }

        // Use BufferSize = 0 to disable the buffer
        public bool BufferEnabled { get; internal set; }

        public int BufferSize { get; set; }
    }
}
