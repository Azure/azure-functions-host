// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using WorkerHarness.Core;
using Channels = System.Threading.Channels;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Grpc.Core;

namespace WorkerHarness
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var workerDirectory = "C:\\temp\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0";
            var workerFile = @"C:\temp\FunctionApp1\FunctionApp1\bin\Debug\net6.0\FunctionApp1.dll";
            var scenarioFile = @"C:\Dev\azure-functions-host\tools\WorkerHarness\src\WorkerHarness.Core\default.scenario";
            //var composeScenarioFile = @"C:\Dev\azure-functions-host\tools\WorkerHarness\src\WorkerHarness.Console\compose.scenario";
            var language = "dotnet-isolated";

            IOptions<WorkerDescription> workerDescription = Options.Create(new WorkerDescription()
            {
                DefaultExecutablePath = Path.Combine(WorkerConstants.ProgramFilesFolder, WorkerConstants.DotnetFolder, WorkerConstants.DotnetExecutableFileName),
                DefaultWorkerPath = workerFile,
                WorkerDirectory = workerDirectory,
                Language = language
            });

            IWorkerProcessBuilder workerProcessBuilder = new WorkerProcessBuilder();

            IGrpcMessageProvider rpcMessageProvider = new GrpcMessageProvider(workerDescription);

            IValidatorFactory validatorFactory = new ValidatorFactory();

            IVariableManager variableManager = new VariableManager();

            Channels.Channel<StreamingMessage> inboundChannel = Channels.Channel.CreateUnbounded<StreamingMessage>();

            Channels.Channel<StreamingMessage> outboundChannel = Channels.Channel.CreateUnbounded<StreamingMessage>();

            IActionWriter actionWriter = new ConsoleWriter();

            IMatch matchService = new StringMatch();

            IActionProvider defaultAtionProvider = new DefaultActionProvider(validatorFactory, matchService,
                rpcMessageProvider, variableManager, inboundChannel, outboundChannel, actionWriter);

            IActionProvider delayActionProvider = new DelayActionProvider();

            List<IActionProvider> actionProviders = new() { defaultAtionProvider, delayActionProvider };

            IScenarioParser scenarioParser = new ScenarioParser(actionProviders);

            IComposingService composingService = new ComposingService();

            GrpcService grpcService = new(inboundChannel, outboundChannel);

            Server server = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new ServerPort(HostConstants.DefaultHostUri, HostConstants.DefaultPort, ServerCredentials.Insecure) }
            };
            server.Start();

            WorkerHarnessExecutor harnessExecutor = new(workerDescription, workerProcessBuilder, scenarioParser, composingService);

            await harnessExecutor.StartScenario(scenarioFile);
        }
    }
}
