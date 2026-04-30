// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Config;
using HttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IScriptWebHookProvider : IWebHookProvider
    {
        bool TryGetHandler(string name, out HttpHandler handler);

        /// <summary>
        /// Returns a value indicating whether the named extension has opted-in to receiving
        /// requests forwarded via the ARM hostruntime extensions bridge. Extensions opt-in by
        /// applying <c>AllowArmWebhookAccessAttribute</c> to their <see cref="IExtensionConfigProvider"/>
        /// implementation.
        /// </summary>
        /// <param name="name">The extension name as registered with the provider.</param>
        /// <returns><c>true</c> if the extension has opted-in; otherwise <c>false</c>.</returns>
        bool IsArmAllowed(string name);
    }
}
