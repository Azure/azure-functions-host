// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class FunctionsServiceProvider : IServiceProvider, IServiceScopeFactory
    {
        private static readonly Rules _defaultContainerRules;
        private readonly Container _root;
        private FunctionsResolver _currentResolver;

        static FunctionsServiceProvider()
        {
            _defaultContainerRules = Rules.Default
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients();
        }

        public FunctionsServiceProvider(IServiceCollection descriptors)
        {
            _root = new Container(rules => _defaultContainerRules);

            _root.Populate(descriptors);
            _root.UseInstance<IServiceProvider>(this);
            _root.UseInstance<FunctionsServiceProvider>(this);

            _currentResolver = new FunctionsResolver(_root, true);
        }

        public string State { get; set; }

        public IServiceProvider ServiceProvider => throw new NotImplementedException();

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
            _root.Populate(services);

            //var results = _root.Validate();
        }

        /// <summary>
        /// Updates the child container and populates it with the services contained in the provided <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="serviceDescriptors">The service descriptors used to populate the child container.</param>
        internal void UpdateChildServices(IServiceCollection serviceDescriptors)
        {
            Rules rules = _defaultContainerRules
                .WithUnknownServiceResolvers(request =>
                {
                    if (request.ServiceType == typeof(IEnumerable<IHostedService>))
                    {
                        return new DelegateFactory(_ => null);
                    }

                    return new DelegateFactory(_ => _root.Resolve(request.ServiceType, IfUnresolved.ReturnDefault));
                });

            var resolver = new Container(rules);
            resolver.Populate(serviceDescriptors);

            var previous = _currentResolver;
            _currentResolver = new FunctionsResolver(resolver);

            if (!previous.IsRootResolver)
            {
                previous.Dispose();
            }

            //var results = resolver.Validate();
        }

        public IServiceScope CreateScope()
        {
            return _currentResolver.CreateChildScope();
        }
    }
}
