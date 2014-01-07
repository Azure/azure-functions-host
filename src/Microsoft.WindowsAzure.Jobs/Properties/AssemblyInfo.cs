using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

[assembly:
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.UnitTests"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.UnitTestsSdk1"),
           InternalsVisibleTo("GauntletTest"),
           InternalsVisibleTo("Microsoft.WindowsAzure.Jobs.Host.Test.Common")
]
