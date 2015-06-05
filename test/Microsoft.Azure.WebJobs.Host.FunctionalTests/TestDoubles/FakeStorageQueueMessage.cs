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
            get 
            { 
                return _message.AsBytes; 
            }
        }

        public override string AsString
        {
            get 
            { 
                return _message.AsString; 
            }
        }

        public override int DequeueCount 
        { 
            get
            {
                return _message.DequeueCount;
            }
            set
            {
                SetMessageProperty("DequeueCount", value);
            }
        }

        public override DateTimeOffset? ExpirationTime
        {
            get
            {
                return _message.ExpirationTime;
            }
            set
            {
                SetMessageProperty("ExpirationTime", value);
            }
        }

        public override string Id
        {
            get
            {
                return _message.Id;
            }
            set
            {
                SetMessageProperty("Id", value);
            }
        }

        public override DateTimeOffset? InsertionTime
        {
            get
            {
                return _message.InsertionTime;
            }
            set
            {
                SetMessageProperty("InsertionTime", value);
            }
        }

        public override DateTimeOffset? NextVisibleTime
        {
            get
            {
                return _message.NextVisibleTime;
            }
            set
            {
                SetMessageProperty("NextVisibleTime", value);
            }
        }

        public override string PopReceipt 
        { 
            get
            {
                return _message.PopReceipt;
            }
            set
            {
                SetMessageProperty("PopReceipt", value);
            }
        }

        public override CloudQueueMessage SdkObject 
        { 
            get 
            { 
                return _message; 
            } 
        }

        private void SetMessageProperty(string propertyName, object value)
        {
            var property = typeof(CloudQueueMessage).GetProperty(propertyName);
            var setter = property.GetSetMethod(true);
            setter.Invoke(_message, new object[] { value });
        }
    }
}
