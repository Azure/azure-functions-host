// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class ApplicationPerformanceCounters
    {
        public int UserTime { get; set; }
        public int KernelTime { get; set; }
        public int PageFaults { get; set; }
        public int Processes { get; set; }
        public int ProcessLimit { get; set; }
        public int Threads { get; set; }
        public int ThreadLimit { get; set; }
        public int Connections { get; set; }
        public int ConnectionLimit { get; set; }
        public int Sections { get; set; }
        public int SectionLimit { get; set; }
        public int NamedPipes { get; set; }
        public int NamedPipeLimit { get; set; }
        public int ReadIoOperations { get; set; }
        public int WriteIoOperations { get; set; }
        public int OtherIoOperations { get; set; }
        public int ReadIoBytes { get; set; }
        public int WriteIoBytes { get; set; }
        public int OtherIoBytes { get; set; }
        public int PrivateBytes { get; set; }
        public int Handles { get; set; }
        public int ContextSwitches { get; set; }
        public int RemoteOpens { get; set; }
    }
}
