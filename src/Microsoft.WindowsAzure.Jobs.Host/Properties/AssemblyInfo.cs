using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

[assembly:
           InternalsVisibleTo("Dashboard"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.UnitTests"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.UnitTestsSdk1"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.UnitTestsSdk2"),
           InternalsVisibleTo("ConsoleApplication1"),
           InternalsVisibleTo("GauntletTest"),
           InternalsVisibleTo("LiveAzureTests"),
           InternalsVisibleTo("RebuildFunctionQueryTables"),
           InternalsVisibleTo("TestLocalHost")
           ]
