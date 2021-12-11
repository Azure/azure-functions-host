// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestScriptEventManager : IScriptEventManager
    {
        public void Publish(ScriptEvent scriptEvent)
        {
        }

        public IDisposable Subscribe(IObserver<ScriptEvent> observer)
        {
            return null;
        }
    }
}