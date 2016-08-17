using System;
using System.Collections.Generic;
using Autofac;
using NCli;

namespace WebJobs.Script.Cli
{
    internal class DependencyResolver : IDependencyResolver
    {
        private IContainer _container;

        public DependencyResolver(IContainer container)
        {
            _container = container;
            RegisterService<IDependencyResolver>(this);
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

        public void RegisterService<T>(object obj)
        {
            var builder = new ContainerBuilder();
            builder.Register(_ => obj).As<T>();
            builder.Update(_container);
        }
    }
}
