using System.IO;
using System.Web.Helpers; // Install nuget package: system.web.helper
using SimpleBatch;

public partial class ImageFuncs
{
    public static void Resize(
        [BlobInput(@"images-input\{name}")] WebImage input,
        [BlobOutput(@"images-output\{name}")] out WebImage output)
    {
        var width = 80;
        var height = 80;

        input.AddTextWatermark("SimpleBatch", fontSize: 6);
        output = input.Resize(width, height);
        
    }
}

