// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Channels = System.Threading.Channels;
using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WorkerHarness.Core;
using WorkerHarness.Core.WorkerProcess;
using WorkerHarness.Core.Commons;
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
            ServiceProvider serviceProvider = SetupDependencyInjection(args);

            // validate user input
            IOptions<HarnessOptions> harnessOptions = serviceProvider.GetRequiredService<IOptions<HarnessOptions>>()!;

            IHarnessOptionsValidate harnessValidate = serviceProvider.GetRequiredService<IHarnessOptionsValidate>();

            if (!harnessValidate.Validate(harnessOptions.Value)) 
            {
                serviceProvider.Dispose();
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

            await harnessExecutor.StartAsync();

            serviceProvider.Dispose();

            return;
        }

        private static ServiceProvider SetupDependencyInjection(string[] args)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("harness.settings.json");
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
                .Configure<HarnessOptions>(config)
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
