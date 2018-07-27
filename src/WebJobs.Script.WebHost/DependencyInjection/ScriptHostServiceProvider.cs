// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class ScriptHostServiceProvider : IServiceProvider, IServiceScopeFactory, ISupportRequiredService, IDisposable
    {
        private const string ScriptJobHostScope = "scriptjobhost";

        private static readonly Rules _defaultContainerRules;
        private readonly IServiceProvider _rootProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly Container _container;
        private ScriptHostScopedResolver _currentResolver;

        static ScriptHostServiceProvider()
        {
            _defaultContainerRules = Rules.Default
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients();
        }

        public ScriptHostServiceProvider(IServiceCollection descriptors, IServiceProvider rootProvider, IServiceScopeFactory rootScopeFactory)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));

            _container = BuildContainer(descriptors);
            _currentResolver = new ScriptHostScopedResolver(_container);
        }

        public IServiceProvider ServiceProvider => throw new NotImplementedException();

        private Container BuildContainer(IServiceCollection descriptors)
        {
            Rules rules = _defaultContainerRules
                .WithUnknownServiceResolvers(request =>
                {
                    if (request.ServiceType == typeof(IEnumerable<IHostedService>))
                    {
                        return new DelegateFactory(_ => null);
                    }

                    return new DelegateFactory(_ => _rootProvider.GetService(request.ServiceType));
                });

            var container = new Container(r => rules);

            container.Populate(descriptors);
            container.UseInstance<IServiceProvider>(this);
            container.UseInstance<ScriptHostServiceProvider>(this);

            return container;
        }

        public object GetService(Type serviceType)
        {
            return GetService(serviceType, IfUnresolved.ReturnDefault);
        }

        public object GetRequiredService(Type serviceType)
        {
            return GetService(serviceType, IfUnresolved.Throw);
        }

        private object GetService(Type serviceType, IfUnresolved ifUnresolved)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            Debug.WriteLine(serviceType.Name);

            return _currentResolver.Container.Resolve(serviceType, ifUnresolved);
        }

        public IServiceScope CreateScope()
        {
            return _currentResolver.CreateChildScope(_rootScopeFactory);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _currentResolver.Dispose();
            }
        }
    }
}
