// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using WorkerHarness.Core;
using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace WorkerHarness
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var scenarioFile = @"C:\Dev\azure-functions-host\tools\WorkerHarness\src\WorkerHarness.Core\default.scenario";

            var serviceProvider = SetupDI();
            var harnessExecutor = serviceProvider.GetService<IWorkerHarnessExecutor>();

            var channel = serviceProvider.GetService<GrpcServiceChannel>()!;


            GrpcService grpcService = new(channel.InboundChannel, channel.OutboundChannel);

            Server server = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new ServerPort(HostConstants.DefaultHostUri, HostConstants.DefaultPort, ServerCredentials.Insecure) }
            };
            server.Start();

            await harnessExecutor!.Start(scenarioFile);
        }


        private static IServiceProvider SetupDI()
        {
            //setup our DI

            var workerDirectory = "C:\\temp\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0";
            var workerFile = @"C:\temp\FunctionApp1\FunctionApp1\bin\Debug\net6.0\FunctionApp1.dll";
            var language = "dotnet-isolated";

            var serviceProvider = new ServiceCollection()
                .AddSingleton<IWorkerProcessBuilder, WorkerProcessBuilder>()
                .AddSingleton<IScenarioParser, ScenarioParser>()
                .AddSingleton<IGrpcMessageProvider, GrpcMessageProvider>()
                .AddSingleton<IValidatorFactory, ValidatorFactory>()
                .AddSingleton<IVariableManager, VariableManager>()
                .AddSingleton<IActionWriter, ConsoleWriter>()
                .AddSingleton<IMatch, StringMatch>()
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
                .Configure<WorkerDescription>(workerDescription =>
                {
                    workerDescription.DefaultExecutablePath = Path.Combine(WorkerConstants.ProgramFilesFolder, WorkerConstants.DotnetFolder, WorkerConstants.DotnetExecutableFileName);
                    workerDescription.DefaultWorkerPath = workerFile;
                    workerDescription.WorkerDirectory = workerDirectory;
                    workerDescription.Language = language;
                })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
