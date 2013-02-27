// This shows 3 equivalent ways of registering a function. 
// Each binding requires:
// - An attribute, eg BlobOutputAttribute
// - a Type. eg, "System.IO.TextWriter"
// This binding pair can be specified several different ways. 
using System.IO;
using SimpleBatch;

namespace IBinderExample
{
    public class BindViaIConfig
    {
        public static void Initialize(IConfiguration config)
        {
            // registering function via configuration uses a Fluent API design pattern
            // Registers function name "Test" in the same class as this configuration method.
            config.Register("Test").TriggerNoAutomatic().Bind("writer", new BlobOutputAttribute(@"Container\Blob.txt"));
        }

        public static void Test(TextWriter writer) // Implicitly via IConfiguratiuon
        {
        }
    }

    public class BindViaParameterAttributes
    {
        [NoAutomaticTrigger] // Via attribute on parameter
        public static void Test2([BlobOutput(@"Container\Blob.txt")] TextWriter writer)
        {
            // Do not combine with IConfiguration technique. 
        }
    }

    public class BindViaIBinder
    {
        [NoAutomaticTrigger] 
        public static void Test3(IBinder binder)
        {
            // This is called every time the function is run. So:
            // - you could pass in values at runtime
            // - you could even call Bind<> in a loop and create an arbitrary number of bindings.
            TextWriter reader = binder.Bind<TextWriter>(new BlobOutputAttribute(@"container\Blob.txt"));
        }
    }
}
