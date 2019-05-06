// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IScriptWebHookProvider : IWebHookProvider
    {
        bool TryGetHandler(string name, out WebhookHttpHandler handler);
    }
}
