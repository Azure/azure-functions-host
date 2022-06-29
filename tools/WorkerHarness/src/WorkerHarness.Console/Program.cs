// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core;
using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace WorkerHarness
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await ExecuteWorkerHarness(args);
        }

        private static async Task ExecuteWorkerHarness(string[] args)
        {
            var serviceProvider = SetupDependencyInjection(args);

            var harnessExecutor = serviceProvider.GetService<IWorkerHarnessExecutor>();

            var channel = serviceProvider.GetService<GrpcServiceChannel>()!;


            GrpcService grpcService = new(channel.InboundChannel, channel.OutboundChannel);

            Server server = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new ServerPort(HostConstants.DefaultHostUri, HostConstants.DefaultPort, ServerCredentials.Insecure) }
            };
            server.Start();

            // TODO: remove the scenarioFile parameter the start method
            await harnessExecutor!.Start(string.Empty);
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
                .AddSingleton<IActionWriter, ConsoleWriter>()
                .AddSingleton<IMatcher, StringMatcher>()
                .AddSingleton<IActionProvider, RpcActionProvider>()
                .AddSingleton<IActionProvider, DelayActionProvider>()
                .AddSingleton<IWorkerHarnessExecutor, DefaultWorkerHarnessExecutor>()
                .AddSingleton<GrpcServiceChannel>(s =>
                {
                    Channels.UnboundedChannelOptions outputOptions = new Channels.UnboundedChannelOptions
                    {
                        SingleWriter = false,
                        SingleReader = true,
                        AllowSynchronousContinuations = true
                    };

                    return new GrpcServiceChannel(Channels.Channel.CreateUnbounded<StreamingMessage>(outputOptions),
                        Channels.Channel.CreateUnbounded<StreamingMessage>(outputOptions));
                })
                .Configure<WorkerDescription>(config)
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
