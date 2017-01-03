using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WebJobs.Script.Cli.Tests.ActionsTests
{
    public class InitActionTests : ActionTestsBase
    {
        public InitActionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InitActionTest()
        {
            // Test
            Program.Main(new[] { "init" });
            var files = Directory.GetFiles(WorkingDirectory).Select(Path.GetFileName);
            var folders = Directory.GetDirectories(WorkingDirectory).Select(Path.GetFileName);

            // Assert
            files.Should().HaveCount(3);
            files.Should().Contain(".gitignore");
            files.Should().Contain("host.json");
            files.Should().Contain("appsettings.json");

            folders.Should().HaveCount(1);
            folders.Should().Contain(".git");
        }
    }
}
