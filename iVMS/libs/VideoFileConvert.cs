using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;



namespace V3SClient.libs
{
    public static class VideoFileConvert
    {
        public static bool ConvertToMp4(List<string> inputPaths, List<string> outputPaths, string ffmpegPath= ".//ffmpeg/bin//ffmpeg.exe")
        {
            //List<Task> allTasks = new List<Task>();

            //for (int idx = 0; idx < inputPaths.Count; idx++)
            //{
            //    if (File.Exists(outputPaths[idx]))
            //        continue;

            //    string args = string.Format("-i \"{0}\" -c copy \"{1}\"", inputPaths[idx], outputPaths[idx]);

            //    Task task = Task.Run(() =>
            //    {
            //        ProcessStartInfo processInfo = new ProcessStartInfo
            //        {
            //            FileName = ffmpegPath,
            //            Arguments = args,
            //            RedirectStandardOutput = true,
            //            RedirectStandardError = true,
            //            CreateNoWindow = true,
            //            UseShellExecute = false
            //        };

            //        using (Process process = new Process { StartInfo = processInfo })
            //        {
            //            process.Start();
            //            process.WaitForExit(); 
            //        }
            //    });

            //    allTasks.Add(task);
            //}

            //Task.WaitAll(allTasks.ToArray());

            //return true;


            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            List<Task> allTask = new List<Task>();
            for (int idx = 0; idx < inputPaths.Count; idx++)
            {
                if (File.Exists(outputPaths[idx]))
                    continue;
                FileInfo fileCheck = new FileInfo((inputPaths[idx]));
                if (fileCheck.Length == 0) continue;
                string args = string.Format("-i {0} -c copy {1}", inputPaths[idx], outputPaths[idx]);
                //$"-i \"{inputPaths[idx]} \" -c copy \"{outputPaths[idx]}\"";

                Task task = Task.Run(() =>
                {

                    processInfo.Arguments = args;
                    Process process = new Process
                    {
                        StartInfo = processInfo
                    };
                    process.Start();
                });
                allTask.Add(task);
            }
            Task.WaitAll(allTask.ToArray());


            return true;
        }
        
    }
}















