using System.IO;
using System.Web.Helpers; // Install nuget package: system.web.helper

namespace ConsoleApplication1
{
    class Program
    {
        // Demonstrate calling Resize directly from console as a batch job
        // This assembly just invokes our Resize() function. Since SimpleBatch will invoke the function
        // directly, we don't need to upload this main assembly to simple batch.
        static void Main(string[] args)
        {
            string inputFile = args[0];
            string outputFile = args[1];

            WebImage input = new WebImage(new FileStream(inputFile, FileMode.Open));
            WebImage output;

            // Do the resize
            ImageFuncs.Resize(input, out output);

            using (var outputStream = new FileStream(outputFile, FileMode.Create))
            {
                var bytes = output.GetBytes();
                outputStream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
