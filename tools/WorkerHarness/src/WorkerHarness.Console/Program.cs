// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.CommandLine;
using WorkerHarness.Core;
using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkerHarness
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var executableFileOption = new Option<string>(
                name: "--workerExecutable",
                description: "The absolute path of a worker executable")
            { IsRequired = true };

            var workerDirectoryOption = new Option<string>(
                name: "--workerDirectory",
                description: "The absolute path of a worker directory")
            { IsRequired = true }; ;

            var languageOption = new Option<string>(
                name: "--language",
                description: "The worker language",
                getDefaultValue: () => string.Empty);

            var scenarioOption = new Option<string>(
                name: "--scenario",
                description: "The absolute path of a scenario file")
            { IsRequired = true }; ;

            var rootCommand = new RootCommand("Execute a scenario file to test a language worker");

            rootCommand.AddOption(scenarioOption);
            rootCommand.AddOption(executableFileOption);
            rootCommand.AddOption(workerDirectoryOption);
            rootCommand.AddOption(languageOption);

            rootCommand.SetHandler(async (scenarioFile, executableFile, workerDirectory, language) =>
            {
                await ExecuteWorkerHarness(scenarioFile, executableFile, workerDirectory, language);
            }, scenarioOption, executableFileOption, workerDirectoryOption, languageOption);

            return await rootCommand.InvokeAsync(args);

        }

        private static async Task ExecuteWorkerHarness(string scenarioFile, string workerExecutable, string workerDirectory, string language)
        {
            var serviceProvider = SetupDependencyInjection(workerExecutable, workerDirectory, language);
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

        private static IServiceProvider SetupDependencyInjection(string workerExecutable, string workerDirectory, string language)
        {
            var serviceProvider = new ServiceCollection()
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
                .Configure<WorkerDescription>(workerDescription =>
                {
                    workerDescription.DefaultExecutablePath = Path.Combine(WorkerConstants.ProgramFilesFolder, WorkerConstants.DotnetFolder, WorkerConstants.DotnetExecutableFileName);
                    workerDescription.DefaultWorkerPath = workerExecutable;
                    workerDescription.WorkerDirectory = workerDirectory;
                    workerDescription.Language = language;
                })
                .AddLogging(c => { c.AddConsole(); })
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
