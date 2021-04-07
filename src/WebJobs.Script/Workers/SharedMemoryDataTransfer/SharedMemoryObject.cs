// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    internal class SharedMemoryObject
    {
        public SharedMemoryObject(string memoryMapName, int count, object value)
        {
            MemoryMapName = memoryMapName;
            Value = value;
            Count = count;
        }

        public string MemoryMapName { get; private set; }

        public int Count { get; private set; }

        public object Value { get; private set; }
    }
}
