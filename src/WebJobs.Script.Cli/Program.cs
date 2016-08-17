// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Library;
using Autofac;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli
{
    internal class Program
    {
        internal readonly static IDependencyResolver DependencyResolver = InitializeDependencyResolve();

        static void Main(string[] args)
        {
            FirstTimeCliExperience();
            ConsoleApp.Run<Program>(args, InitializeDependencyResolve());
        }

        private static void FirstTimeCliExperience()
        {
            var settings = new PersistentSettings();
            if (settings.RunFirstTimeCliExperience)
            {
                //ColoredConsole.WriteLine("Welcome to Azure Functions CLI");
                //settings.RunFirstTimeCliExperience = false;
            }
        }

        public static bool RelaunchSelfElevated(string command, out string errors)
        {
            errors = string.Empty;
            command = command ?? string.Empty;

            var logFile = Path.GetTempFileName();
            var exeName = Process.GetCurrentProcess().MainModule.FileName;
            command = $"/c \"{exeName} {command} >> {logFile}\"";


            var startInfo = new ProcessStartInfo("cmd")
            {
                Verb = "runas",
                Arguments = command,
                WorkingDirectory = Environment.CurrentDirectory,
                CreateNoWindow = false,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            process.WaitForExit();
            errors = File.ReadAllText(logFile);
            return process.ExitCode == ExitCodes.Success;
        }

        private static IDependencyResolver InitializeDependencyResolve()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(c => new PersistentAuthHelper { AzureEnvironments = AzureEnvironments.Prod })
                .As<IAuthHelper>();

            builder.Register(c => new AzureClient(retryCount: 3, authHelper: c.Resolve<IAuthHelper>()))
                .As<IAzureClient>();

            builder.RegisterType<ArmManager>()
                .As<IArmManager>();

            builder.RegisterType<ProcessManager>()
                .As<IProcessManager>();

            builder.RegisterType<SecretsManager>()
                .As<ISecretsManager>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance()
                .ExternallyOwned();

            builder.RegisterType<TipsManager>()
                .As<ITipsManager>();

            return new DependencyResolver(builder.Build());
        }
    }
}
