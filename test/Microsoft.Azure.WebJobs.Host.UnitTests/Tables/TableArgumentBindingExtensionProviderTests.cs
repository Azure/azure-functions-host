// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Tables
{
    public class TableArgumentBindingExtensionProviderTests
    {
        private static CloudTable BoundTable = null;

        private ParameterInfo[] _parameters;

        public TableArgumentBindingExtensionProviderTests()
        {
            _parameters = this.GetType().GetMethod("Parameters", BindingFlags.NonPublic | BindingFlags.Static).GetParameters();
        }

        [Fact]
        public void TryCreate_DelegatesToExtensions()
        {
            DefaultExtensionRegistry extensions = new DefaultExtensionRegistry();
            TableArgumentBindingExtensionProvider provider = new TableArgumentBindingExtensionProvider(extensions);

            // before binding extensions are registered for these types,
            // the provider returns null
            
            Assert.Null(provider.TryCreate(_parameters[0]));
            Assert.Null(provider.TryCreate(_parameters[1]));
            Assert.Null(provider.TryCreate(_parameters[2]));

            // register the binding extensions
            FooBarTableArgumentBindingProvider fooBarExtensionProvider = new FooBarTableArgumentBindingProvider();
            BazTableArgumentBindingProvider bazExtensionProvider = new BazTableArgumentBindingProvider();
            extensions.RegisterExtension<IArgumentBindingProvider<ITableArgumentBinding>>(fooBarExtensionProvider);
            extensions.RegisterExtension<IArgumentBindingProvider<ITableArgumentBinding>>(bazExtensionProvider);
            provider = new TableArgumentBindingExtensionProvider(extensions);

            IStorageTableArgumentBinding binding = provider.TryCreate(_parameters[0]);
            Assert.Same(typeof(IFoo), binding.ValueType);

            binding = provider.TryCreate(_parameters[1]);
            Assert.Same(typeof(IBar), binding.ValueType);

            binding = provider.TryCreate(_parameters[2]);
            Assert.Same(typeof(IBaz), binding.ValueType);
        }

        [Fact]
        public async Task TryCreate_ReturnsTableArgumentBindingExtensionWrapper()
        {
            DefaultExtensionRegistry extensions = new DefaultExtensionRegistry();
            FooBarTableArgumentBindingProvider fooBarExtensionProvider = new FooBarTableArgumentBindingProvider();
            extensions.RegisterExtension<IArgumentBindingProvider<ITableArgumentBinding>>(fooBarExtensionProvider);

            TableArgumentBindingExtensionProvider provider = new TableArgumentBindingExtensionProvider(extensions);

            IStorageTableArgumentBinding binding = provider.TryCreate(_parameters[0]);
            Assert.Equal(typeof(TableArgumentBindingExtensionProvider.TableArgumentBindingExtension), binding.GetType());

            Assert.Null(BoundTable);
            CloudTable table = new CloudTable(new Uri("http://localhost:10000/test/table"));
            IStorageTable storageTable = new StorageTable(table);
            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new StringWriter());
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            IValueProvider valueProvider = await binding.BindAsync(storageTable, context);
            Assert.NotNull(valueProvider);
            Assert.Same(table, BoundTable);
        }

        private static void Parameters(IFoo foo, IBar bar, IBaz baz) { }

        private interface IFoo
        {
        }

        private interface IBar
        {
        }

        private interface IBaz
        {
        }

        private class FooBarTableArgumentBindingProvider : IArgumentBindingProvider<ITableArgumentBinding>
        {
            public ITableArgumentBinding TryCreate(ParameterInfo parameter)
            {
                if (parameter.ParameterType == typeof(IFoo) ||
                    parameter.ParameterType == typeof(IBar))
                {
                    return new FooBarTableArgumentBinding(parameter.ParameterType);
                }

                return null;
            }

            internal class FooBarTableArgumentBinding : ITableArgumentBinding
            {
                private Type _valueType;

                public FooBarTableArgumentBinding(Type valueType)
                {
                    _valueType = valueType;
                }

                public FileAccess Access
                {
                    get { return FileAccess.ReadWrite; }
                }

                public Type ValueType
                {
                    get { return _valueType; }
                }

                public static object BindValue { get; set; }

                public Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
                {
                    BoundTable = value;
                    return Task.FromResult<IValueProvider>(new FooBarValueProvider());
                }
            }

            internal class FooBarValueProvider : IValueProvider
            {
                public Type Type
                {
                    get { throw new NotImplementedException(); }
                }

                public object GetValue()
                {
                    throw new NotImplementedException();
                }

                public string ToInvokeString()
                {
                    throw new NotImplementedException();
                }
            }
        }

        private class BazTableArgumentBindingProvider : IArgumentBindingProvider<ITableArgumentBinding>
        {
            public ITableArgumentBinding TryCreate(ParameterInfo parameter)
            {
                if (parameter.ParameterType == typeof(IBaz))
                {
                    return new BazTableArgumentBinding();
                }

                return null;
            }

            internal class BazTableArgumentBinding : ITableArgumentBinding
            {
                public FileAccess Access
                {
                    get { return FileAccess.ReadWrite; }
                }

                public Type ValueType
                {
                    get { return typeof(IBaz); }
                }

                public Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}
