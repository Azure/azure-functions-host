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

            await harnessExecutor.Start();

            serviceProvider.Dispose();

            return;
        }

        private static ServiceProvider SetupDependencyInjection(string[] args)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("harness.settings.json");
            IConfiguration config = configurationBuilder.Build();

            ServiceProvider serviceProvider = new ServiceCollection()
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
                .AddSingleton<IHarnessOptionsValidate, HarnessOptionsValidate>()
                .Configure<HarnessOptions>(config)
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
