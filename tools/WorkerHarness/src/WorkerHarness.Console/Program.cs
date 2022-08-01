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

namespace WorkerHarness
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (!TryGetHarnessSetting(out string harnessSettingsPath))
            {
                return;
            }

            ServiceProvider serviceProvider = SetupDependencyInjection(harnessSettingsPath);

            // validate user input
            IOptions<HarnessOptions> harnessOptions = serviceProvider.GetRequiredService<IOptions<HarnessOptions>>()!;

            IHarnessOptionsValidate harnessValidate = serviceProvider.GetRequiredService<IHarnessOptionsValidate>();

            if (!harnessValidate.Validate(harnessOptions.Value)) 
            {
                serviceProvider.Dispose();
                return;
            }

            // start the grpc server
            var grpcServer = serviceProvider.GetRequiredService<IGrpcServer>();
            grpcServer.Start();

            // run the harness
            var harnessExecutor = serviceProvider.GetRequiredService<IWorkerHarnessExecutor>();
            await harnessExecutor.StartAsync();

            // clean up
            await grpcServer.Shutdown();
            serviceProvider.Dispose();

            return;
        }

        private static ServiceProvider SetupDependencyInjection(string harnessSettingsPath)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile(harnessSettingsPath);
            IConfiguration config = configurationBuilder.Build();

            ServiceProvider serviceProvider = new ServiceCollection()
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
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }

        private static bool TryGetHarnessSetting(out string harnessSettingPath)
        {
            string MissingHarnessSettingJsonFile = "Missing the required harness.settings.json file in the current directory.";

            harnessSettingPath = Path.Combine(Directory.GetCurrentDirectory(), "harness.settings.json");

            if (!File.Exists(harnessSettingPath))
            {
                System.Console.WriteLine(MissingHarnessSettingJsonFile);
                return false;
            }

            return true;
        }
    }
}
