// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Standby
{
    public class StandbyChangeTokenSource : IOptionsChangeTokenSource<ScriptApplicationHostOptions>
    {
        public string Name { get; set; }

        public IChangeToken GetChangeToken()
        {
            return StandbyManager.ChangeToken;
        }
    }
}
