// This attribute will not be resolve-able because the Razor.Runtime assembly will not be
// present in the output directory. This is used by tests to ensure we don't crash.
[assembly: Microsoft.AspNetCore.Razor.Hosting.RazorExtensionAssemblyName("something", "something")]