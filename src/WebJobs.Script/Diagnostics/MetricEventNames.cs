// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public static class MetricEventNames
    {
        // host level events
        public const string ApplicationStartLatency = "host.application.start";
        public const string ApplicationInsightsEnabled = "host.applicationinsights.enabled";
        public const string ApplicationInsightsDisabled = "host.applicationinsights.disabled";
        public const string HostStartupLatency = "host.startup.latency";
        public const string HostStartupReadFunctionMetadataLatency = "host.startup.readfunctionmetadata.latency";
        public const string HostStartupInitializeBindingProvidersLatency = "host.startup.initializebindingproviders.latency";
        public const string HostStartupCreateMetadataProviderLatency = "host.startup.createmetadataprovider.latency";
        public const string HostStartupGetFunctionDescriptorsLatency = "host.startup.getfunctiondescriptors.latency";
        public const string HostStartupGrpcServerLatency = "host.startup.outofproc.grpcserver.initialize.latency";
        public const string HostStartupRuntimeLanguage = "host.startup.runtime.language.{0}";

        // language worker level events
        public const string WorkerInitializeLatency = "host.startup.outofproc.{0}worker.initialize.attempt{1}.latency";

        // function level events
        public const string FunctionInvokeLatency = "function.invoke.latency";
        public const string FunctionBindingTypeFormat = "function.binding.{0}";
        public const string FunctionBindingTypeDirectionFormat = "function.binding.{0}.{1}";
        public const string FunctionCompileLatencyByLanguageFormat = "function.compile.{0}.latency";
        public const string FunctionInvokeThrottled = "function.invoke.throttled";
        public const string FunctionUserLog = "function.userlog";
        public const string FunctionInvokeSucceeded = "function.invoke.succeeded";
        public const string FunctionInvokeFailed = "function.invoke.failed";

        // secret managment events
        public const string SecretManagerDeleteSecret = "secretmanager.deletesecret.{0}";
        public const string SecretManagerGetFunctionSecrets = "secretmanager.getfunctionsecrets.{0}";
        public const string SecretManagerGetHostSecrets = "secretmanager.gethostsecrets.{0}";
        public const string SecretManagerAddOrUpdateFunctionSecret = "secretmanager.addorupdatefunctionsecret.{0}";
        public const string SecretManagerSetMasterKey = "secretmanager.setmasterkey.{0}";
        public const string SecretManagerPurgeOldSecrets = "secretmanager.purgeoldsecrets.{0}";

        // Linux container specialization events
        public const string LinuxContainerSpecializationZipExtract = "linux.container.specialization.zip.extract";
        public const string LinuxContainerSpecializationZipDownload = "linux.container.specialization.zip.download";
        public const string LinuxContainerSpecializationZipWrite = "linux.container.specialization.zip.write";
        public const string LinuxContainerSpecializationZipHead = "linux.container.specialization.zip.head";
        public const string LinuxContainerSpecializationFuseMount = "linux.container.specialization.mount";
        public const string LinuxContainerSpecializationMSIInit = "linux.container.specialization.msi.init";
        public const string LinuxContainerSpecializationUnsquash = "linux.container.specialization.unsquash";
        public const string LinuxContainerSpecializationFileCommand = "linux.container.specialization.file.command";
        public const string LinuxContainerSpecializationAzureFilesMount = "linux.container.specialization.azure.files.mount";
    }
}
