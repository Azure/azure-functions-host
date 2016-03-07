// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;
using Dashboard.HostMessaging;
using Dashboard.Data.Logs;

namespace Dashboard.Data
{
    internal class NullLogReader : IPersistentQueueReader<PersistentQueueMessage>
    {
        public int Count(int? limit)
        {
            return 0;
        }

        public void Delete(PersistentQueueMessage message)
        {
            throw new NotImplementedException();
        }

        public PersistentQueueMessage Dequeue()
        {
            return null;
        }

        public void TryMakeItemVisible(PersistentQueueMessage message)
        {
            throw new NotImplementedException();
        }
    }
}