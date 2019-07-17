// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.FileProvisioning;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class DependencyValidator : IDependencyValidator
    {
        private static readonly ExpectedDependencyBuilder _expectedDependencies = CreateExpectedDependencies();

        private static ExpectedDependencyBuilder CreateExpectedDependencies()
        {
            var expected = new ExpectedDependencyBuilder();

            expected.ExpectNone<IScriptEventManager>();
            expected.ExpectNone<IEventGenerator>();

            expected.Expect<ILoggerFactory, ScriptLoggerFactory>();
            expected.Expect<IMetricsLogger, WebHostMetricsLogger>();

            expected.Expect<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();

            expected.Expect<IEventCollectorFactory>("Microsoft.Azure.WebJobs.Logging.EventCollectorFactory");

            expected.ExpectCollection<IEventCollectorProvider>()
                .Expect<FunctionInstanceLogCollectorProvider>()
                .Expect("Microsoft.Azure.WebJobs.Logging.FunctionResultAggregatorProvider");

            expected.ExpectCollection<IHostedService>()
                .Expect<JobHostService>("Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService")
                .Expect<PrimaryHostCoordinator>()
                .Expect<HttpInitializationService>()
                .Expect<FileMonitoringService>()
                .Expect<LanguageWorkerConsoleLogService>()
                .Optional<FunctionsScaleMonitorService>()
                .Optional<FuncAppFileProvisioningService>() // Used by powershell.
                .Optional<JobHostService>() // Missing when host is offline.
                .Optional<FunctionsSyncService>(); // Conditionally registered.

            expected.ExpectSubcollection<ILoggerProvider>()
                .Expect<AzureMonitorDiagnosticLoggerProvider>()
                .Expect<FunctionFileLoggerProvider>()
                .Expect<HostFileLoggerProvider>()
                .Expect<SystemLoggerProvider>()
                .Expect<UserLogMetricsLoggerProvider>();

            return expected;
        }

        public virtual void Validate(IServiceCollection services)
        {
            StringBuilder sb = new StringBuilder();

            foreach (InvalidServiceDescriptor invalidDescriptor in _expectedDependencies.FindInvalidServices(services))
            {
                sb.AppendLine();
                sb.Append($"  [{invalidDescriptor.Reason}] {FormatServiceDescriptor(invalidDescriptor.Descriptor)}");
            }

            if (sb.Length > 0)
            {
                string msg = $"The following service registrations did not match the expected services:{sb.ToString()}";
                throw new InvalidHostServicesException(msg);
            }
        }

        private static string FormatServiceDescriptor(ServiceDescriptor descriptor)
        {
            string format = $"{nameof(descriptor.ServiceType)}: {descriptor.ServiceType}, {nameof(descriptor.Lifetime)}: {descriptor.Lifetime}";

            if (descriptor.ImplementationFactory != null)
            {
                format += $", {nameof(descriptor.ImplementationFactory)}: {descriptor.ImplementationFactory}";
            }

            if (descriptor.ImplementationInstance != null)
            {
                format += $", {nameof(descriptor.ImplementationInstance)}: {descriptor.ImplementationInstance}";
            }

            if (descriptor.ImplementationType != null)
            {
                format += $", {nameof(descriptor.ImplementationType)}: {descriptor.ImplementationType}";
            }

            return format;
        }
    }
}
