using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.TaskClient.Protocol;

namespace Common
{
    public static class TaskUtils
    {
        public static string GetFileContent(Stream stream)
        {
            StringBuilder sb = new StringBuilder();

            const int bufferSize = 1024 * 1024 * 4;
            byte[] buffer = new byte[bufferSize];
            int offset = 0;
            int bytesRead = stream.Read(buffer, offset, bufferSize);
            while (bytesRead > 0)
            {
                sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                bytesRead = stream.Read(buffer, 0, bufferSize);
            }

            return sb.ToString();
        }

        public static void UploadFileToBlob(String fileName, String containerSAS)
        {
            var x = Path.Combine(Environment.CurrentDirectory, fileName);
            var f = File.Exists(x);

            Console.WriteLine("Uploading {0} to {1}", fileName, containerSAS);
            CloudBlobContainer container = new CloudBlobContainer(containerSAS);
            CloudBlob blob = container.GetBlobReference(fileName);
            blob.UploadFile(x);
        }

        public static string ConstructBlobSource(string container, string blob)
        {
            int index = container.IndexOf("?");

            if (index != -1)
            {
                //SAS                
                string containerAbsoluteUrl = container.Substring(0, index);
                return containerAbsoluteUrl + "/" + blob + container.Substring(index);
            }
            else
            {
                return container + "/" + blob;
            }
        }

        public static void PrintExitCodeOrSchedulingError(Task task)
        {
            Console.Write("Task {0} reached completed state. ", task.Name);
            if (task.ExecutionInfo.ExitCode != null)
            {
                Console.WriteLine("Exit Code = {0}", task.ExecutionInfo.ExitCode.Value);
            }
            else if (task.ExecutionInfo.SchedulingError != null)
            {
                SchedulingError error = task.ExecutionInfo.SchedulingError;
                Console.WriteLine("Scheduling error code {0} category {1} message {2}",
                    error.Code, error.Category, error.Message);
            }
        }

        public static void ReadStdErrOrOutputBasedOnExitCode(String workitemName, String jobName,
            Task task, TaskRequestDispatcher taskRequestDispatcher)
        {
            //If task succesfully executed. read stdout.txt file otherwise read stderr.txt
            if (task.ExecutionInfo.ExitCode != null)
            {
                PrintFile("stdout.txt", workitemName, jobName, task, taskRequestDispatcher);

                if (task.ExecutionInfo.ExitCode.Value != 0)
                {
                    PrintFile("stderr.txt", workitemName, jobName, task, taskRequestDispatcher);
                }                
            }
        }

        static void PrintFile(string fileName, String workitemName, String jobName,
            Task task, TaskRequestDispatcher taskRequestDispatcher)
        {
            Console.WriteLine("Reading file {0}", fileName);

            GetFileResponse file = taskRequestDispatcher.GetTaskFile(
                workitemName, jobName, task.Name, fileName);

            Console.WriteLine("{0}", TaskUtils.GetFileContent(file.FileContentStream));
        }

        public static void ReadStdErrIfFailed(String workitemName, String jobName,
            Task task, TaskRequestDispatcher taskRequestDispatcher)
        {
            //If task failed with non-zero exit code read stderr.txt
            if (task.ExecutionInfo.ExitCode != null &&
                task.ExecutionInfo.ExitCode.Value != 0)
            {
                String fileName = "stderr.txt";

                Console.WriteLine("Reading file {0}", fileName);

                GetFileResponse file = taskRequestDispatcher.GetTaskFile(
                    workitemName, jobName, task.Name, fileName);

                Console.WriteLine("{0}", TaskUtils.GetFileContent(file.FileContentStream));
            }
        }
    }
}
