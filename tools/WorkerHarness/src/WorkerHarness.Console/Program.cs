// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core;
using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace WorkerHarness
{
    public class Program
    {
        private static readonly string UserOptionErrorMessage = "Invalid or missing --{0} argument";

        public static async Task Main(string[] args)
        {
            var serviceProvider = SetupDependencyInjection(args);

            ILogger<Program> logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // validate user input
            IOptions<HarnessOptions> harnessOptions = serviceProvider.GetRequiredService<IOptions<HarnessOptions>>()!;

            if (!ValidUserArguments(harnessOptions.Value, logger))
            {
                return;
            }

            // run WorkerHarness
            var harnessExecutor = serviceProvider.GetRequiredService<IWorkerHarnessExecutor>();

            var channel = serviceProvider.GetRequiredService<GrpcServiceChannel>()!;

            GrpcService grpcService = new(channel.InboundChannel, channel.OutboundChannel);

            Server server = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new ServerPort(HostConstants.DefaultHostUri, HostConstants.DefaultPort, ServerCredentials.Insecure) }
            };
            server.Start();

            await harnessExecutor.Start();

        }

        private static bool ValidUserArguments(HarnessOptions harnessOptions, ILogger logger)
        {
            bool valid = true;

            if (string.IsNullOrEmpty(harnessOptions.ScenarioFile) || !File.Exists(harnessOptions.ScenarioFile))
            {
                logger.LogError(UserOptionErrorMessage, "scenarioFile");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.WorkerExecutable) || !File.Exists(harnessOptions.WorkerExecutable))
            {
                logger.LogError(UserOptionErrorMessage, "workerExecutable");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.LanguageExecutable) || !File.Exists(harnessOptions.LanguageExecutable))
            {
                logger.LogError(UserOptionErrorMessage, "languageExecutable");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.WorkerDirectory) || !Directory.Exists(harnessOptions.WorkerDirectory))
            {
                logger.LogError(UserOptionErrorMessage, "workerDirectory");
                valid = false;
            }

            Task.Delay(500).Wait();

            return valid;
        }

        private static IServiceProvider SetupDependencyInjection(string[] args)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddCommandLine(args);
            IConfiguration config = configurationBuilder.Build();

            IServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton<IWorkerProcessBuilder, WorkerProcessBuilder>()
                .AddSingleton<IScenarioParser, ScenarioParser>()
                .AddSingleton<IGrpcMessageProvider, GrpcMessageProvider>()
                .AddSingleton<IValidatorFactory, ValidatorFactory>()
                .AddSingleton<IVariableObservable, VariableManager>()
                .AddSingleton<IMatcher, StringMatcher>()
                .AddSingleton<IActionProvider, RpcActionProvider>()
                .AddSingleton<IActionProvider, DelayActionProvider>()
                .AddSingleton<IWorkerHarnessExecutor, DefaultWorkerHarnessExecutor>()
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
                .Configure<HarnessOptions>(config)
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
