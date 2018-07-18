// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class FunctionsServiceProvider : IServiceProvider, IServiceScopeFactory
    {
        private static readonly Rules _defaultContainerRules;
        private readonly Container _container;
        private ScriptHostScopedResolver _currentResolver;

        static FunctionsServiceProvider()
        {
            _defaultContainerRules = Rules.Default
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients();
        }

        public FunctionsServiceProvider(IServiceCollection descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            _container = new Container(_defaultContainerRules);
            _container.Populate(descriptors);
            _container.UseInstance<IServiceProvider>(this);
            _container.UseInstance<IServiceScopeFactory>(this);

            _currentResolver = new ScriptHostScopedResolver(_container);
        }

        public object GetService(Type serviceType)
        {
            return _container.Resolve(serviceType, IfUnresolved.ReturnDefault);
        }

        public IServiceScope CreateScope()
        {
            return new FunctionsServiceScope(_container.OpenScope());
        }
    }
}
