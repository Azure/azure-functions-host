// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Verbs.List
{
    internal abstract class BaseListVerb : BaseVerb
    {
        public BaseListVerb(ITipsManager tipsManager) : base(tipsManager)
        {
        }
    }
}
