using SimpleBatch;
using System.Web.Helpers; // Install nuget package: system.web.helper
using System.IO;

public partial class ImageFuncs
{
    // a lot like ASP.Net's Global.asax...
    // SimpleBatch will search for a function named "Initialize" under the same declaring type as the function we invoke (Resize).
    // Must have this signature. 
    public static void Initialize(IConfiguration config)
    {
        // Register a custom model binder for the given type.
        config.Add<WebImage>(new WebImageBinder());
    }

    // A custom model binder for binding between a stream and a WebImage.
    public class WebImageBinder : ICloudBlobStreamBinder<WebImage>
    {
        public WebImage ReadFromStream(Stream input)
        {
            return new WebImage(input);
        }

        public void WriteToStream(WebImage result, Stream output)
        {
            var bytes = result.GetBytes();
            output.Write(bytes, 0, bytes.Length);
        }
    }
}