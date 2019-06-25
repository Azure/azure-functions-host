// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Eventing.Host
{
    public class ScriptHostStateChangedEvent : ScriptEvent
    {
        public ScriptHostStateChangedEvent(ScriptHostState oldState, ScriptHostState newState)
            : base(nameof(ScriptHostStateChangedEvent), EventSources.ScriptHostState)
        {
            OldState = oldState;
            NewState = newState;
        }

        public ScriptHostState OldState { get; }

        public ScriptHostState NewState { get; }
    }
}
