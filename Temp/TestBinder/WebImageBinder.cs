using SimpleBatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Helpers;

namespace TestBinder
{
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
