// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.BindingExtensionBundle;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    public class ScriptStartupTypeLocatorFactory : IScriptStartupTypeLocatorFactory
    {
        private readonly IExtensionBundleManager _extensionBundleManager;
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;

        public ScriptStartupTypeLocatorFactory(IExtensionBundleManager extensionBundleManager, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
        }

        public IWebJobsStartupTypeLocator CreateStartupTypeLocator()
        {
            return new ScriptStartupTypeLocator(_applicationHostOptions.Value.ScriptPath, _extensionBundleManager);
        }
    }
}
