// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Logging;
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

            Assert.Equal(2, names.Count);
            Assert.Equal("mscorlib", names[0]);
            Assert.Equal("System", names[1]);
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
        public void LoggingPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(ILogWriter).Assembly;

            var expected = new[]
            {
                "FunctionId",
                "ActivationEvent",
                "FunctionInstanceLogItem",
                "FunctionInstanceStatus",
                "FunctionStatusExtensions",
                "FunctionVolumeTimelineEntry",
                "IAggregateEntry",
                "IFunctionDefinition",
                "IFunctionInstanceBaseEntry",
                "IFunctionInstanceBaseEntryExtensions",
                "ILogReader",
                "ILogWriter",
                "ILogTableProvider",
                "InstanceCountEntity",
                "IRecentFunctionEntry",
                "LogFactory",
                "ProjectionHelper",
                "RecentFunctionQuery",
                "Segment`1"
            };

            AssertPublicTypes(expected, assembly);
        }


        [Fact]
        public void ServiceBusPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(ServiceBusAttribute).Assembly;

            var expected = new[]
            {
                "EventHubAttribute",
                "EventHubConfiguration",
                "EventHubJobHostConfigurationExtensions",
                "EventHubTriggerAttribute",
                "EventHubAsyncCollector",
                "MessageProcessor",
                "MessagingProvider",
                "ServiceBusAccountAttribute",
                "ServiceBusAttribute",
                "ServiceBusConfiguration",
                "ServiceBusJobHostConfigurationExtensions",
                "ServiceBusTriggerAttribute"
            };

            AssertPublicTypes(expected, assembly);
        }

        [Fact]
        public void WebJobsPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(QueueTriggerAttribute).Assembly;

            var expected = new[]
            {
                "IAttributeInvokeDescriptor`1",
                "AutoResolveAttribute",
                "AppSettingAttribute",
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
                "TableAttribute",
                "SingletonAttribute",
                "SingletonMode",
                "SingletonScope",
                "StorageAccountAttribute",
                "DisableAttribute",
                "TimeoutAttribute",
                "TraceLevelAttribute",
                "ODataFilterResolutionPolicy"
            };

            AssertPublicTypes(expected, assembly);
        }

        [Fact]
        public void WebJobsHostPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(Microsoft.Azure.WebJobs.JobHost).Assembly;

            var expected = new[]
            {
                "DefaultNameResolver",
                "FunctionInstanceLogEntry",
                "IConverter`2",
                "IAsyncConverter`2",
                "IConverterManager",
                "IConverterManagerExtensions",
                "FuncConverter`3",
                "BindingFactory",
                "ITriggerBindingStrategy`2",
                "ConnectionStringNames",
                "JobHost",
                "JobHostConfiguration",
                "JobHostQueuesConfiguration",
                "JobHostBlobsConfiguration",
                "IJobActivator",
                "ITypeLocator",
                "INameResolver",
                "WebJobsShutdownWatcher",
                "BindingContext",
                "BindingProviderContext",
                "BindingTemplate",
                "BindStepOrder",
                "OpenType",
                "FunctionBindingContext",
                "IBinding",
                "IBindingProvider",
                "IExtensionConfigProvider",
                "IExtensionRegistry",
                "IListener",
                "IOrderedValueBinder",
                "ITriggerBinding",
                "ITriggerBindingProvider",
                "ITriggerData",
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
                "ListenerFactoryContext",
                "BindingTemplateSource",
                "TriggeredFunctionData",
                "ExtensionConfigContext",
                "ExtensionConfigContextExtensions",
                "IQueueProcessorFactory",
                "QueueProcessorFactoryContext",
                "QueueProcessor",
                "FunctionResult",
                "IArgumentBinding`1",
                "IArgumentBindingProvider`1",
                "SingletonConfiguration",
                "TraceWriter",
                "JobHostTraceConfiguration",
                "StorageClientFactory",
                "StorageClientFactoryContext",
                "BindingDataProvider",
                "IBindingDataProvider",
                "FunctionInvocationException",
                "TraceEvent",
                "BindingTemplateExtensions",
                "FunctionIndexingException",
                "Binder",
                "IWebJobsExceptionHandler",
                "WebJobsExceptionHandler",
                "FunctionTimeoutException",
                "PoisonMessageEventArgs",
                "IResolutionPolicy",
                "RecoverableException",
                "FunctionException",
                "FunctionListenerException",
                "ExceptionFormatter"
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
