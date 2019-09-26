// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection.DryIoc
{
    public class AsyncScopeContext : IScopeContext
    {
        private static AsyncLocal<IScope> _asyncLocalScope = new AsyncLocal<IScope>();

        public IScope GetCurrentOrDefault()
        {
            return _asyncLocalScope.Value;
        }

        public IScope SetCurrent(SetCurrentScopeHandler setCurrentScope)
        {
            if (setCurrentScope == null)
            {
                throw new ArgumentNullException(nameof(setCurrentScope));
            }

            var oldScope = GetCurrentOrDefault();
            var newScope = setCurrentScope(oldScope);
            _asyncLocalScope.Value = newScope;
            return newScope;
        }

        public void Dispose()
        {
        }
    }
}
