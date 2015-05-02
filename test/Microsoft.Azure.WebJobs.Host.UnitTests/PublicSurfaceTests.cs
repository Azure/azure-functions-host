// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    /// <summary>
    /// These tests help maintain our public surface area + dependencies. They will
    /// fail any time new dependencies or public surface area are added, ensuring
    /// we review such additions carefully.
    /// </summary>
    public class PublicSurfaceTests
    {
        [Fact]
        public void AssemblyReferences_InJobsAssembly()
        {
            // The DLL containing the binding attributes should be truly minimal and have no extra dependencies. 
            var names = GetAssemblyReferences(typeof(QueueTriggerAttribute).Assembly);

            Assert.Equal(1, names.Count);
            Assert.Equal("mscorlib", names[0]);
        }

        [Fact]
        public void AssemblyReferences_InJobsHostAssembly()
        {
            var names = GetAssemblyReferences(typeof(JobHost).Assembly);

            foreach (var name in names)
            {
                if (name.StartsWith("Microsoft.WindowsAzure"))
                {
                    // Only azure dependency is on the storage sdk
                    Assert.Equal("Microsoft.WindowsAzure.Storage", name);
                }
            }
        }

        [Fact]
        public void WebJobsPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(QueueTriggerAttribute).Assembly;

            var expected = new[]
            {
                "BinderExtensions",
                "BlobAttribute",
                "BlobTriggerAttribute",
                "IBinder",
                "IAsyncCollector`1",
                "ICollector`1",
                "ICloudBlobStreamBinder`1",
                "NoAutomaticTriggerAttribute",
                "QueueAttribute",
                "QueueTriggerAttribute",
                "TableAttribute"
            };

            AssertPublicTypes(expected, assembly);
        }

        [Fact]
        public void WebJobsHostPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(Microsoft.Azure.WebJobs.JobHost).Assembly;

            var expected = new[]
            {
                "ConnectionStringNames",
                "JobHost",
                "JobHostConfiguration",
                "JobHostQueuesConfiguration",
                "IJobActivator",
                "ITypeLocator",
                "INameResolver",
                "WebJobsShutdownWatcher",
                "AsyncConverter`2",
                "BindingContext",
                "BindingDataPathHelper",
                "BindingDataProvider",
                "BindingProviderContext",
                "BindingTemplate",
                "BindStepOrder",
                "CompositeObjectToTypeConverter`1",
                "ConversionResult`1",
                "FunctionBindingContext",
                "IArgumentBinding`1",
                "IAsyncConverter`2",
                "IAsyncObjectToTypeConverter`1",
                "IBinding",
                "IBindingDataProvider",
                "IBindingProvider",
                "IConverter`2",
                "IdentityConverter`1",
                "IExtensionConfigProvider",
                "IExtensionRegistry",
                "IListener",
                "IListenerFactory",
                "IObjectToTypeConverter`1",
                "IOrderedValueBinder",
                "ITriggerBinding",
                "ITriggerBinding`1",
                "ITriggerBindingProvider",
                "ITriggerData",
                "ITriggerDataArgumentBinding`1",
                "IValueBinder",
                "IValueProvider",
                "NameResolverExtensions",
                "FunctionDescriptor",
                "ParameterDescriptor",
                "ParameterDisplayHints",
                "TriggerBindingProviderContext",
                "TriggerData",
                "TriggerParameterDescriptor",
                "ValueBindingContext",
                "AmbientConnectionStringProvider",
                "IExtensionRegistryExtensions",
                "ITriggeredFunctionExecutor",
                "ITriggeredFunctionExecutor`1",
                "ListenerFactoryContext",
                "BindingTemplateSource"
            };

            AssertPublicTypes(expected, assembly);
        }

        private static List<string> GetAssemblyReferences(Assembly assembly)
        {
            var assemblyRefs = assembly.GetReferencedAssemblies();
            var names = (from assemblyRef in assemblyRefs
                         orderby assemblyRef.Name.ToLowerInvariant()
                         select assemblyRef.Name).ToList();
            return names;
        }

        private static void AssertPublicTypes(IEnumerable<string> expected, Assembly assembly)
        {
            var publicTypes = (assembly.GetExportedTypes()
                .Select(type => type.Name)
                .OrderBy(n => n));

            AssertPublicTypes(expected.ToArray(), publicTypes.ToArray());
        }

        private static void AssertPublicTypes(string[] expected, string[] actual)
        {
            var newlyIntroducedPublicTypes = actual.Except(expected).ToArray();

            if (newlyIntroducedPublicTypes.Length > 0)
            {
                string message = String.Format("Found {0} unexpected public type{1}: \r\n{2}",
                    newlyIntroducedPublicTypes.Length,
                    newlyIntroducedPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", newlyIntroducedPublicTypes));
                Assert.True(false, message);
            }

            var missingPublicTypes = expected.Except(actual).ToArray();

            if (missingPublicTypes.Length > 0)
            {
                string message = String.Format("missing {0} public type{1}: \r\n{2}",
                    missingPublicTypes.Length,
                    missingPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", missingPublicTypes));
                Assert.True(false, message);
            }
        }
    }
}
