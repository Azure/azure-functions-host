// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class DefaultExtensionRegistryTests
    {
        [Fact]
        public void RegisterExtension_ThrowsArgumentNull_WhenTypeIsNull()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterExtension(null, new ServiceA())
            );
            Assert.Equal("type", exception.ParamName);
        }

        [Fact]
        public void RegisterExtension_ThrowsArgumentNull_WhenInstanceIsNull()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterExtension(typeof(IMyService), null)
            );
            Assert.Equal("instance", exception.ParamName);
        }

        [Fact]
        public void GetExtensions_ThrowsArgumentNull_WhenTypeIsNull()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => registry.GetExtensions(null)
            );
            Assert.Equal("type", exception.ParamName);
        }

        [Fact]
        public void RegisterExtension_ThrowsArgumentOutOfRange_WhenInstanceNotInstanceOfType()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => registry.RegisterExtension(typeof(IMyService), new ServiceD())
            );
            Assert.Equal("instance", exception.ParamName);
        }

        [Fact]
        public void RegisterExtension_RegisterMultipleInstances()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();

            ServiceA serviceA = new ServiceA();
            ServiceB serviceB = new ServiceB();
            ServiceC serviceC = new ServiceC();
            registry.RegisterExtension(typeof(IMyService), serviceA);
            registry.RegisterExtension(typeof(IMyService), serviceB);
            registry.RegisterExtension(typeof(IMyService), serviceC);

            OtherServiceA otherServiceA = new OtherServiceA();
            OtherServiceB otherServiceB = new OtherServiceB();
            registry.RegisterExtension(typeof(IMyOtherService), otherServiceA);
            registry.RegisterExtension(typeof(IMyOtherService), otherServiceB);

            object[] services = registry.GetExtensions(typeof(IMyService)).ToArray();
            Assert.Equal(3, services.Length);
            Assert.True(services.Contains(serviceA));
            Assert.True(services.Contains(serviceB));
            Assert.True(services.Contains(serviceC));

            services = registry.GetExtensions(typeof(IMyOtherService)).ToArray();
            Assert.Equal(2, services.Length);
            Assert.True(services.Contains(otherServiceA));
            Assert.True(services.Contains(otherServiceB));
        }

        [Fact]
        public void IExtensionRegistryExtensions_RegisterAndRetrieve()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();

            // use the generic extension methods to register
            ServiceA serviceA = new ServiceA();
            ServiceB serviceB = new ServiceB();
            ServiceC serviceC = new ServiceC();
            registry.RegisterExtension<IMyService>(serviceA);
            registry.RegisterExtension<IMyService>(serviceB);
            registry.RegisterExtension<IMyService>(serviceC);

            IMyService[] services = registry.GetExtensions<IMyService>().ToArray();
            Assert.Equal(3, services.Length);
            Assert.True(services.Contains(serviceA));
            Assert.True(services.Contains(serviceB));
            Assert.True(services.Contains(serviceC));
        }

        [Fact]
        public void GetExtensions_ReturnsEmptyCollection_WhenServiceTypeNotFound()
        {
            DefaultExtensionRegistry registry = new DefaultExtensionRegistry();

            object[] services = registry.GetExtensions(typeof(IConvertible)).ToArray();
            Assert.Equal(0, services.Length);
        }

        public interface IMyService
        {
            void DoIt();
        }

        public class ServiceA : IMyService
        {
            public void DoIt() { }
        }

        public class ServiceB : IMyService
        {
            public void DoIt() { }
        }

        public class ServiceC : IMyService
        {
            public void DoIt() { }
        }

        // Not an IMyService
        public class ServiceD
        {
        }

        public interface IMyOtherService
        {
            void DoIt();
        }

        public class OtherServiceA : IMyOtherService
        {
            public void DoIt() { }
        }

        public class OtherServiceB : IMyOtherService
        {
            public void DoIt() { }
        }
    }
}
