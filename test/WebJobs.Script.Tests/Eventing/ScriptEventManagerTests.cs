// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class ScriptEventManagerTests
    {
        [Fact]
        public async Task Publish_NotifiesSubscribers()
        {
            var eventManager = new ScriptEventManager();

            var events1 = new List<ScriptEvent>();
            var events2 = new List<ScriptEvent>();
            eventManager.Subscribe(e => events1.Add(e));
            eventManager.Subscribe(e => events2.Add(e));

            var eventCount = 10;
            for (int i = 0; i < eventCount; i++)
            {
                eventManager.Publish(new ScriptEvent(i.ToString(), string.Empty));
            }

            await TestHelpers.Await(() => events1.Count == eventCount && events2.Count == eventCount);

            for (int i = 0; i < eventCount; i++)
            {
                Assert.Equal(i.ToString(), events1[i].Name);
                Assert.Equal(i.ToString(), events2[i].Name);
            }
        }

        [Fact]
        public async Task DisposingOfSubscription_StopsNotifications()
        {
            var eventManager = new ScriptEventManager();

            var events1 = new List<ScriptEvent>();
            var events2 = new List<ScriptEvent>();
            eventManager.Subscribe(e => events1.Add(e));
            IDisposable subscription = eventManager.Subscribe(e => events2.Add(e));

            var eventCount = 5;
            for (int i = 0; i < eventCount; i++)
            {
                eventManager.Publish(new ScriptEvent(i.ToString(), string.Empty));
            }

            subscription.Dispose();

            for (int i = 0; i < eventCount; i++)
            {
                eventManager.Publish(new ScriptEvent(i.ToString(), string.Empty));
            }

            await TestHelpers.Await(() => events1.Count == eventCount * 2 && events2.Count == eventCount);

            Assert.Equal(eventCount * 2, events1.Count);
            Assert.Equal(eventCount, events2.Count);
        }
    }
}
