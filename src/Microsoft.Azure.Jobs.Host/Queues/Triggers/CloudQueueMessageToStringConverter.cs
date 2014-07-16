// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class CloudQueueMessageToStringConverter : IConverter<CloudQueueMessage, string>
    {
        public string Convert(CloudQueueMessage input)
        {
            return input.AsString;
        }
    }
}
