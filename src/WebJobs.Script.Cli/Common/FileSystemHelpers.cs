using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Common
{
    internal static class FileSystemHelpers
    {
        private static IFileSystem _default = new FileSystem();
        private static IFileSystem _instance;

        public static IFileSystem Instance
        {
            get { return _instance ?? _default; }
            set { _instance = value; }
        }

        public static Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
        {
            return Instance.File.Open(path, mode, access, share);
        }

        internal static byte[] ReadAllBytes(string path)
        {
            return Instance.File.ReadAllBytes(path);
        }

        public static string ReadAllTextFromFile(string path)
        {
            return Instance.File.ReadAllText(path);
        }

        public static async Task<string> ReadAllTextFromFileAsync(string path)
        {
            using (var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var streamReader = new StreamReader(fileStream))
            {
                return await streamReader.ReadToEndAsync();
            }
        }

        public static void WriteAllTextToFile(string path, string content)
        {
            Instance.File.WriteAllText(path, content);
        }

        public static async Task WriteAllTextToFileAsync(string path, string content)
        {
            using (var fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var streamWriter = new StreamWriter(fileStream))
            {
                await streamWriter.WriteAsync(content);
                await streamWriter.FlushAsync();
            }
        }

        internal static void WriteAllBytes(string path, byte[] bytes)
        {
            Instance.File.WriteAllBytes(path, bytes);
        }

        public static bool FileExists(string path)
        {
            return Instance.File.Exists(path);
        }

        public static bool DirectoryExists(string path)
        {
            return Instance.Directory.Exists(path);
        }

        public static void CreateDirectory(string path)
        {
            Instance.Directory.CreateDirectory(path);
        }

        public static string EnsureDirectory(string path)
        {
            if (!DirectoryExists(path))
            {
                CreateDirectory(path);
            }
            return path;
        }
    }
}
