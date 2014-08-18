// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Dashboard.Data;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.HostMessaging
{
    public class Aborter : IAborter
    {
        private readonly IHostMessageSender _sender;
        private readonly IAbortRequestLogger _logger;

        public Aborter(IHostMessageSender sender, IAbortRequestLogger logger)
        {
            _sender = sender;
            _logger = logger;
        }

        public void RequestHostInstanceAbort(string queueName)
        {
            _sender.Enqueue(queueName, new AbortHostInstanceMessage());
            _logger.LogAbortRequest(queueName);
        }

        public bool HasRequestedHostInstanceAbort(string queueName)
        {
            return _logger.HasRequestedAbort(queueName);
        }
    }
}
