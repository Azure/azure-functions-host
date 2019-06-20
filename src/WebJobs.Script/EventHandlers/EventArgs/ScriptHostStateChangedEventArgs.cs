// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.EventHandlers.EventArgs
{
    public class ScriptHostStateChangedEventArgs
    {
        public ScriptHostStateChangedEventArgs(ScriptHostState oldValue, ScriptHostState newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }

        public ScriptHostState OldValue { get; private set; }

        public ScriptHostState NewValue { get; private set; }
    }
}