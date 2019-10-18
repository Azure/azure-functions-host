// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.WebJobs.Script.Http
{
    internal class DefaultHttpRouteManager : IHttpRoutesManager
    {
        public void InitializeHttpFunctionRoutes(IScriptJobHost host)
        {
            // noop
        }
    }
}
