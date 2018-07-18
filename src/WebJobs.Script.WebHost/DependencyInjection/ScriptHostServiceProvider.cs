// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class ScriptHostServiceProvider : IServiceProvider, IServiceScopeFactory
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
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            return _currentResolver.Container.Resolve(serviceType, IfUnresolved.ReturnDefault);
        }

        public void AddServices(IServiceCollection services)
        {
            _container.Populate(services);

            //var results = _root.Validate();
        }

        /// <summary>
        /// Updates the child container and populates it with the services contained in the provided <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="serviceDescriptors">The service descriptors used to populate the child container.</param>
        internal void UpdateChildServices(IServiceCollection serviceDescriptors)
        {
            //_container.Unregister<IHostedService>(condition: f => f.ImplementationType == typeof(WebJobsScriptHostService));
            var resolver = new Container(_defaultContainerRules); // (Container)_root.OpenScope(ScriptJobHostScope);
            resolver.Populate(serviceDescriptors, singletonReuse: Reuse.InCurrentNamedScope(ScriptJobHostScope));

            var functionsResolver = new ScriptHostScopedResolver(resolver);
            ScriptHostScopedResolver previous = Interlocked.Exchange(ref _currentResolver, functionsResolver);

            if (!previous.IsRootResolver)
            {
                previous.Dispose();
            }

            //var results = resolver.Validate();
        }

        public IServiceScope CreateScope()
        {
            return _currentResolver.CreateChildScope(_rootScopeFactory);
        }
    }
}
