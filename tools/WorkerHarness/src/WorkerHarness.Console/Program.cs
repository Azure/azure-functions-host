// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WorkerHarness.Core;
using WorkerHarness.Core.WorkerProcess;
using WorkerHarness.Core.Options;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.Matching;
using WorkerHarness.Core.Validators;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Diagnostics;
using Newtonsoft.Json;
using WorkerHarness.Core.Profiling;

namespace WorkerHarness
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            HarnessEventSource.Log.AppStarted();
            ServiceProvider? serviceProvider = null;
            IGrpcServer? grpcServer = null;
            try
            {
                Console.WriteLine($"Starting worker harness version {GetHarnessVersion()} at {DateTime.Now}");

                if (!TryGetHarnessSetting(out string harnessSettingsPath))
                {
                    return;
                }

                serviceProvider = SetupDependencyInjection(harnessSettingsPath);

                // validate user input
                IOptions<HarnessOptions> harnessOptions = serviceProvider.GetRequiredService<IOptions<HarnessOptions>>()!;

                IHarnessOptionsValidate harnessValidate = serviceProvider.GetRequiredService<IHarnessOptionsValidate>();

                if (!harnessValidate.Validate(harnessOptions.Value))
                {
                    await serviceProvider.DisposeAsync();
                    return;
                }

                // start the grpc server
                grpcServer = serviceProvider.GetRequiredService<IGrpcServer>();
                grpcServer.Start();

                // run the harness
                var harnessExecutor = serviceProvider.GetRequiredService<IWorkerHarnessExecutor>();
                await harnessExecutor.StartAsync();

                int waitTime = harnessOptions.Value.WaitBeforeExitingInSeconds;
                Console.WriteLine($"Will wait for {waitTime} seconds before exiting.");
                await Task.Delay(TimeSpan.FromSeconds(waitTime));
                Console.WriteLine("Exiting...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Exiting at {DateTime.Now}");
                if (grpcServer is not null)
                {
                    await grpcServer.Shutdown();
                }
                serviceProvider?.Dispose();
            }
        }

        private static ServiceProvider SetupDependencyInjection(string harnessSettingsPath)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile(harnessSettingsPath);
            IConfiguration config = configurationBuilder.Build();

            var c = new ServiceCollection()
                .AddSingleton<IWorkerProcessBuilder, SystemProcessBuilder>()
                .AddSingleton<IScenarioParser, ScenarioParser>()
                .AddSingleton<IStreamingMessageProvider, StreamingMessageProvider>()
                .AddSingleton<IPayloadVariableSolver, PayloadVariableSolver>()
                .AddSingleton<IValidatorFactory, ValidatorFactory>()
                .AddSingleton<IVariableObservable, VariableManager>()
                .AddSingleton<IMessageMatcher, MessageMatcher>()
                .AddSingleton<IContextMatcher, ContextMatcher>()
                .AddSingleton<IActionProvider, RpcActionProvider>()
                .AddSingleton<IActionProvider, DelayActionProvider>()
                .AddSingleton<IActionProvider, ImportActionProvider>()
                .AddSingleton<IActionProvider, TerminateActionProvider>()
                .AddSingleton<IWorkerHarnessExecutor, WorkerHarnessExecutor>()
                .AddSingleton<IProfilerFactory, ProfilerFactory>()
                .AddSingleton<GrpcServiceChannel>(s =>
                {
                    Channels.UnboundedChannelOptions outputOptions = new()
                    {
                        SingleWriter = false,
                        SingleReader = true,
                        AllowSynchronousContinuations = true
                    };

                    return new GrpcServiceChannel(Channels.Channel.CreateUnbounded<StreamingMessage>(outputOptions),
                        Channels.Channel.CreateUnbounded<StreamingMessage>(outputOptions));
                })
                .AddSingleton<IHarnessOptionsValidate, HarnessOptionsValidate>()
                .AddSingleton<IGrpcServer, GrpcServer>()
                .Configure<HarnessOptions>(config)
                .AddLogging(builder => builder.AddConsole());

            if (OperatingSystem.IsWindows())
            {
                var perfviewConfig = GetPerfviewConfig();
                if (perfviewConfig is not null)
                {
                    c.AddSingleton(perfviewConfig);
                }

            }
            ServiceProvider serviceProvider = c.BuildServiceProvider();

            return serviceProvider;
        }

        private static bool TryGetHarnessSetting(out string harnessSettingPath)
        {
            // harness.settings file location convention:
            // 1. App root
            // 2. configs directory
            string MissingHarnessSettingJsonFile = "Missing the required harness.settings.json file in the current directory.";

            harnessSettingPath = Path.Combine(Directory.GetCurrentDirectory(), "harness.settings.json");
            if (!File.Exists(harnessSettingPath))
            {
                // If not in the root, check for a configs directory
                harnessSettingPath = Path.Combine(Directory.GetCurrentDirectory(), "configs", "harness.settings.json");
            }

            if (!File.Exists(harnessSettingPath))
            {
                Console.WriteLine(MissingHarnessSettingJsonFile);
                Console.WriteLine(harnessSettingPath);
                return false;
            }

            return true;
        }

        private static string? GetHarnessVersion()
        {
            const string version = Constants.WorkerHarnessVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
            return null;
        }

        private static PerfviewConfig? GetPerfviewConfig()
        {
            PerfviewConfig? config = new();
            var configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "perfviewconfig.json");
            if (File.Exists(configFilePath))
            {
                var configJson = File.ReadAllText(configFilePath);
                config = JsonConvert.DeserializeObject<PerfviewConfig>(configJson);
            }

            return config;
        }
    }
}
