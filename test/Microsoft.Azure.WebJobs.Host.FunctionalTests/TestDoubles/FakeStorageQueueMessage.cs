// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageQueueMessage : MutableStorageQueueMessage
    {
        private readonly CloudQueueMessage _message;

        public FakeStorageQueueMessage(CloudQueueMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            _message = message;
            DequeueCount = message.DequeueCount;
            ExpirationTime = message.ExpirationTime;
            Id = message.Id;
            InsertionTime = message.InsertionTime;
            NextVisibleTime = message.NextVisibleTime;
            PopReceipt = message.PopReceipt;
        }

        public override byte[] AsBytes
        {
            get { return _message.AsBytes; }
        }

        public override string AsString
        {
            get { return _message.AsString; }
        }

        public override int DequeueCount { get; set; }

        public override DateTimeOffset? ExpirationTime { get; set; }

        public override string Id { get; set; }

        public override DateTimeOffset? InsertionTime { get; set; }

        public override DateTimeOffset? NextVisibleTime { get; set; }

        public override string PopReceipt { get; set; }

        public override CloudQueueMessage SdkObject
        {
            get { return _message; }
        }
    }
}
