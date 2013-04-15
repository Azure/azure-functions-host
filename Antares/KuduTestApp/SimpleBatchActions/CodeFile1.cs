using System.IO;
using SimpleBatch;

public class Test
{
    public static void Copy(
        [BlobInput(@"kudu-test\{name}.input.txt")] TextReader input,
        [BlobOutput(@"kudu-test\{name}.output.txt")] TextWriter output)
    {
        string content = input.ReadToEnd();
        output.Write(content);
    }
}
