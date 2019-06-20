// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.EventHandlers.EventArgs;

namespace Microsoft.Azure.WebJobs.Script.EventHandlers
{
    public delegate void ScriptHostStateChangedEventHandler(object sender, ScriptHostStateChangedEventArgs e);
}