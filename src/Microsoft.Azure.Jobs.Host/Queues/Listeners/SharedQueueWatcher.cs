// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class SharedQueueWatcher : IMessageEnqueuedWatcher
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<INotificationCommand>> _registrations =
            new ConcurrentDictionary<string, ConcurrentBag<INotificationCommand>>();

        public void Notify(string enqueuedInQueueName)
        {
            ConcurrentBag<INotificationCommand> queueRegistrations;

            if (_registrations.TryGetValue(enqueuedInQueueName, out queueRegistrations))
            {
                foreach (INotificationCommand registration in queueRegistrations.ToArray())
                {
                    registration.Notify();
                }
            }
        }

        public void Register(string queueName, INotificationCommand notification)
        {
            _registrations.AddOrUpdate(queueName,
                new ConcurrentBag<INotificationCommand>(new INotificationCommand[] { notification }),
                (i, existing) => { existing.Add(notification); return existing; });
        }
    }
}
