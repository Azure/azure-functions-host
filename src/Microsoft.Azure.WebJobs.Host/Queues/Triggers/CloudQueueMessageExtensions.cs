// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal static class CloudQueueMessageExtensions
    {
        public static string TryGetAsString(this CloudQueueMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            string value;

            try
            {
                value = message.AsString;
            }
            catch (DecoderFallbackException)
            {
                // when the message type is binary, AsString will throw if the bytes aren't valid UTF-8.
                value = null;
            }

            return value;
        }
    }
}
