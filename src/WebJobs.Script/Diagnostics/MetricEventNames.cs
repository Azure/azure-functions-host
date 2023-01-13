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

        // Script host level events
        public const string ScriptHostManagerBuildScriptHost = "scripthostmanager.buildscripthost.latency";
        public const string ScriptHostManagerStartScriptHost = "scripthostmanager.startscripthost.latency";
        public const string ScriptHostManagerStartService = "scripthostmanager.start.latency";
        public const string ScriptHostManagerRestartService = "scripthostmanager.restart.latency";

        // language worker level events
        public const string WorkerInitializeLatency = "host.startup.outofproc.{0}worker.initialize.attempt{1}.latency";
        public const string WorkerInvoked = "{0}worker.invoke";
        public const string WorkerInvokeSucceeded = "{0}worker.invoke.succeeded";
        public const string WorkerInvokeFailed = "{0}worker.invoke.failed";

        // FunctionMetadataprovider events
        public const string ReadFunctionsMetadata = "functionmetadataprovider.readfunctionsmetadata";
        public const string ReadFunctionMetadata = "functionmetadataprovider.readfunctionsmetadata.readfunctionmetadata.{0}";

        // Host json file configuration events
        public const string LoadHostConfigurationSource = "hostjsonfileconfigurationsource.loadhostconfigurationsource";
        public const string LoadHostConfiguration = "hostjsonfileconfigurationsource.loadhostconfigurationsource.loadhostconfig";
        public const string InitializeHostConfiguration = "hostjsonfileconfigurationsource.loadhostconfigurationsource.initializehostconfig";

        // LanguageWorkerChannel events
        public const string FunctionLoadRequestResponse = "rpcworkerchannel.functionloadrequestresponse";
        public const string WorkerMetadata = "rpcworkerchannel.workerinitresponse.workermetadata";

        // ScriptStartupTypeLocator events
        public const string ParseExtensions = "ScriptStartupTypeLocator.ParseExtensions";

        // Worker configuration events
        public const string GetConfigs = "workerconfigfactory.getconfigs";
        public const string AddProvider = "workerconfigfactory.getconfigs.buildworkerproviderdictionary.addprovider.{0}";

        // function level events
        public const string FunctionInvokeLatency = "function.invoke.latency";
        public const string FunctionBindingTypeFormat = "function.binding.{0}";
        public const string FunctionBindingDeferred = "function.binding.deferred";
        public const string FunctionCompileLatencyByLanguageFormat = "function.compile.{0}.latency";
        public const string FunctionInvokeThrottled = "function.invoke.throttled";
        public const string FunctionUserLog = "function.userlog";
        public const string FunctionInvokeSucceeded = "function.invoke.succeeded";
        public const string FunctionInvokeFailed = "function.invoke.failed";

        // Http worker events
        public const string CustomHandlerConfiguration = "hostjsonfileconfigurationsource.customhandler";
        public const string DelayUntilWorkerIsInitialized = "httpworkerchannel.delayuntilworkerisinitialized";

        // Out of proc process events
        public const string ProcessStart = "WorkerProcess.Start";

        // secret managment events
        public const string SecretManagerDeleteSecret = "secretmanager.deletesecret.{0}";
        public const string SecretManagerGetFunctionSecrets = "secretmanager.getfunctionsecrets.{0}";
        public const string SecretManagerGetHostSecrets = "secretmanager.gethostsecrets.{0}";
        public const string SecretManagerAddOrUpdateFunctionSecret = "secretmanager.addorupdatefunctionsecret.{0}";
        public const string SecretManagerSetMasterKey = "secretmanager.setmasterkey.{0}";
        public const string SecretManagerPurgeOldSecrets = "secretmanager.purgeoldsecrets.{0}";

        // Linux container specialization events
        public const string LinuxContainerSpecializationBindMount = "linux.container.specialization.bind.mount";
        public const string LinuxContainerSpecializationMountCifs = "linux.container.specialization.mount.cifs";
        public const string LinuxContainerSpecializationZipExtract = "linux.container.specialization.zip.extract";
        public const string LinuxContainerSpecializationZipDownload = "linux.container.specialization.zip.download";
        public const string LinuxContainerSpecializationZipDownloadUsingManagedIdentity = "linux.container.specialization.zip.download.mi.token";
        public const string LinuxContainerSpecializationZipDownloadWarmup = "linux.container.specialization.zip.download.warmup";
        public const string LinuxContainerSpecializationZipWrite = "linux.container.specialization.zip.write";
        public const string LinuxContainerSpecializationZipWriteWarmup = "linux.container.specialization.zip.write.warmup";
        public const string LinuxContainerSpecializationZipMountCopy = "linux.container.specialization.zip.mountcopy";
        public const string LinuxContainerSpecializationZipMountCopyWarmup = "linux.container.specialization.zip.mountcopy.warmup";
        public const string LinuxContainerSpecializationZipHead = "linux.container.specialization.zip.head";
        public const string LinuxContainerSpecializationZipHeadWarmup = "linux.container.specialization.zip.head.warmup";
        public const string LinuxContainerSpecializationFuseMount = "linux.container.specialization.mount";
        public const string LinuxContainerSpecializationMSIInit = "linux.container.specialization.msi.init";
        public const string LinuxContainerSpecializationFetchMIToken = "linux.container.specialization.fetch.mi.token";
        public const string LinuxContainerSpecializationUnsquash = "linux.container.specialization.unsquash";
        public const string LinuxContainerSpecializationFileCommand = "linux.container.specialization.file.command";
        public const string LinuxContainerSpecializationAzureFilesMount = "linux.container.specialization.azure.files.mount";
        public const string LinuxContainerSpecializationGetPackageType = "linux.container.specialization.get.package.type";
        public const string LinuxContainerSpecializationBYOSMountPrefix = "linux.container.specialization.byos";

        // Specialization events
        public const string SpecializationSpecializeHost = "specialization.standbymanager.specializehost";
        public const string SpecializationStandbyManagerInitialize = "specialization.standbymanager.initialize";
        public const string SpecializationLanguageWorkerChannelManagerSpecialize = "specialization.webhostrpcworkerchannelmanager.specialize";
        public const string SpecializationEnvironmentReloadRequestResponse = "specialization.webhostrpcworkerchannel.sendfunctionenvironmentreloadrequest.functionenvironmentreloadresponse";
        public const string SpecializationScheduleShutdownStandbyChannels = "specialization.scheduleshutdownstandbychannels";
        public const string SpecializationRestartHost = "specialization.scripthostmanager.restarthost";
        public const string SpecializationDelayUntilHostReady = "specialization.scripthostmanager.delayuntilhostready";
        public const string SpecializationShutdownStandbyChannels = "specialization.webhostrpcworkerchannelmanager.scheduleshutdownstandbychannels.{0}";
        public const string SpecializationShutdownStandbyChannel = "specialization.webhostrpcworkerchannelmanager.scheduleshutdownstandbychannels.Worker.{0}";
    }
}
