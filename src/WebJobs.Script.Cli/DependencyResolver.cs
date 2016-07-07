using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Autofac;
using Microsoft.Azure.WebJobs.Host;
using NCli;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli
{
    internal class DependencyResolver : IDependencyResolver
    {
        private readonly IContainer _container;

        public DependencyResolver(IContainer container)
        {
            _container = container;
        }

        public DependencyResolver(IDictionary<object, Type> instances)
        {
            var builder = new ContainerBuilder();

            foreach (var pair in instances)
            {
                builder.Register(_ => pair.Key).As(pair.Value);
            }

            _container = builder.Build();
        }

        public object GetService(Type type)
        {
            return _container.Resolve(type);
        }

        public T GetService<T>()
        {
            return _container.Resolve<T>();
        }
    }
}
