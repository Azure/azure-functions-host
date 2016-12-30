using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using WebJobs.Script.Cli.Actions;
using WebJobs.Script.Cli.Actions.AzureActions;
using WebJobs.Script.Cli.Actions.HostActions;
using WebJobs.Script.Cli.Actions.LocalActions;
using Xunit;

namespace WebJobs.Script.Cli.Tests.ActionsTests
{
    public class ResolveActionTests
    {
        [Theory]
        [InlineData("azure functionapp enable-git-repo appName", typeof(EnableGitRepoAction))]
        [InlineData("azure functionapp fetch-app-settings appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure functionapp fetch appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure get-publish-username", typeof(GetPublishUserNameAction))]
        [InlineData("azure account list", typeof(ListAzureAccountsAction))]
        [InlineData("azure subscriptions list", typeof(ListAzureAccountsAction))]
        [InlineData("azure functionapp list", typeof(ListFunctionAppsAction))]
        [InlineData("azure storage list", typeof(ListStorageAction))]
        [InlineData("azure login", typeof(LoginAction))]
        [InlineData("azure logout", typeof(LogoutAction))]
        [InlineData("azure functionapp logstream appName", typeof(LogStreamAction))]
        [InlineData("azure portal appName", typeof(PortalAction))]
        [InlineData("azure account set accountName", typeof(SetAzureAccountAction))]
        [InlineData("azure set-publish-password userName", typeof(SetPublishPasswordAction))]
        [InlineData("azure set-publish-username userName", typeof(SetPublishPasswordAction))]
        [InlineData("host start", typeof(StartHostAction))]
        [InlineData("host stop", typeof(StopHostAction))]
        [InlineData("settings add settingName", typeof(AddSettingAction))]
        [InlineData("settings add-storage-account storageName", typeof(AddStorageAccountSettingAction))]
        [InlineData("new", typeof(CreateFunctionAction))]
        [InlineData("function new", typeof(CreateFunctionAction))]
        [InlineData("function create", typeof(CreateFunctionAction))]
        [InlineData("settings decrypt", typeof(DecryptSettingAction))]
        [InlineData("settings encrypt", typeof(EncryptSettingsAction))]
        [InlineData("settings delete settingName", typeof(DeleteSettingAction))]
        [InlineData("settings list", typeof(ListSettingsAction))]
        [InlineData("init", typeof(InitAction))]
        [InlineData("create", typeof(InitAction))]
        [InlineData("functionapp init", typeof(InitAction))]
        [InlineData("functionapp create", typeof(InitAction))]
        [InlineData("run functionName", typeof(RunFunctionAction))]
        [InlineData("function run functionName", typeof(RunFunctionAction))]
        [InlineData("-v", typeof(HelpAction))]
        [InlineData("-version", typeof(HelpAction))]
        [InlineData("--version", typeof(HelpAction))]
        [InlineData("", typeof(HelpAction))]
        [InlineData("help", typeof(HelpAction))]
        [InlineData("WrongName", typeof(HelpAction))]
        public void ResolveCommandLineCorrectly(string args, Type type)
        {
            var container = Program.InitializeAutofacContainer();
            var app = new ConsoleApp(args.Split(' ').ToArray(), type.Assembly, container);
            var result = app.Parse();
            result.Should().BeOfType(type);
        }
    }
}
