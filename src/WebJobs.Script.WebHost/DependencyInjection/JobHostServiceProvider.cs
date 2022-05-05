// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class JobHostServiceProvider : IServiceProvider, IServiceScopeFactory, ISupportRequiredService, IDisposable
    {
        private static readonly Rules _defaultContainerRules;
        private static readonly Setup _jobHostRootScopeFactorySetup;
        private readonly IServiceProvider _rootProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly Container _container;
        private ScopedResolver _currentResolver;

        static JobHostServiceProvider()
        {
            _jobHostRootScopeFactorySetup = Setup.With(preventDisposal: true);
            _defaultContainerRules = Rules.Default
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients();
        }

        public JobHostServiceProvider(IServiceCollection descriptors, IServiceProvider rootProvider, IServiceScopeFactory rootScopeFactory)
        {
            ArgumentNullException.ThrowIfNull(descriptors);

            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));

            _container = BuildContainer(descriptors);
            _currentResolver = new ScopedResolver(_container);
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

                    return new DelegateFactory(_ => _rootProvider.GetService(request.ServiceType), setup: _jobHostRootScopeFactorySetup);
                });

            // preferInterpretation will be set to true to significanly improve cold start in consumption mode
            // it will be set to false for premium and appservice plans to make sure throughput is not impacted
            // there is no throughput drop in consumption with this setting.
            var preferInterpretation = SystemEnvironment.Instance.IsConsumptionSku() ? true : false;
            var container = new Container(r => rules, preferInterpretation: preferInterpretation);

            container.Populate(descriptors);

            // If a scoped IServiceProvider is present, use it.
            container.RegisterDelegate<IServiceProvider>(r => (r as Container).ScopedServiceProvider ?? this);

            container.UseInstance<IServiceScopeFactory>(this);
            container.UseInstance<JobHostServiceProvider>(this);

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
            try
            {
                return _currentResolver.CreateChildScope(_rootScopeFactory);
            }
            catch (ContainerException ex) when (ex.Error == Error.ContainerIsDisposed)
            {
                throw new HostDisposedException(_currentResolver.GetType().FullName, ex);
            }
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
