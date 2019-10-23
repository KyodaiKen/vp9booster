using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VP9Booster
{
    public class FFMPEGTaskManager
    {
        public delegate void FinishedEventHandler(object sender, EventArgs e);
        public delegate void ProgressChangedEventHandler(object sender, EventArgs e);
        public event FinishedEventHandler OnAllFinished;
        public event ProgressChangedEventHandler OnProgressChanged;
        private ulong taskCount;
        private ulong tasksRunning;
        public ulong TaskCount { get => taskCount; }
        public ulong TasksRunnung { get => tasksRunning; }

        private List<FFMPEGTask> tasks;
        private bool allExited;

        public struct FFMPEGTask
        {
            public ulong id { get; set; }
            public Process Process { get; set; }
            public ulong Progress { get; set; }
            public bool HasExited { get; set; }
        }

        public FFMPEGTaskManager()
        {
            allExited = false;
            tasks = new List<FFMPEGTask>();
        }

        public ulong Progress {
            get
            {
                if (tasks != null)
                {
                    ulong totalProgress = 0;
                    for(var i=0;i<tasks.Count;i++)
                    {
                        var t = tasks[i];
                        totalProgress += t.Progress;
                        //Console.WriteLine(t.id + ": " + t.Progress);
                    }
                    return totalProgress;
                }
                else return 0;
            }
        }

        private void CheckAllExited()
        {
            if (tasks != null)
            {
                allExited = true;
                foreach (var t in tasks)
                {
                    if (t.HasExited == false)
                    {
                        allExited = false;
                        break;
                    }
                }
                if (allExited && OnAllFinished != null) OnAllFinished(this, new EventArgs());
            }
        }

        public bool RunFFMPEGTask(string arguments)
        {
            FFMPEGTask task = new FFMPEGTask();
            task.id = taskCount++;
            task.Process = new Process() {
                EnableRaisingEvents = true
              , StartInfo = new ProcessStartInfo() {
                  RedirectStandardOutput = true
                , RedirectStandardError = true
                , UseShellExecute = false
                , CreateNoWindow = true
                , FileName = "ffmpeg"
                , Arguments = arguments
              }
            };

            void PrcsOutputHndlr(object process, DataReceivedEventArgs e)
            {
                string currentLine = e.Data;
                if (currentLine != null)
                {
                    //Get total number of frames converted so far
                    string framesShelf = "";
                    ulong resultedFrames;
                    if (currentLine.StartsWith("frame="))
                    {
                        framesShelf = currentLine.Substring(6, currentLine.IndexOf(" ")).TrimStart();
                        bool longParseResult = ulong.TryParse(framesShelf, out resultedFrames);
                        if (longParseResult)
                        {
                            for(var i=0;i<tasks.Count;i++)
                            {
                                if(tasks[i].id == task.id)
                                {
                                    var t = tasks[i];
                                    t.Progress = resultedFrames;
                                    tasks[i] = t;
                                }
                            }
                        }
                        OnProgressChanged(process, new EventArgs());
                    }
                }
            }

            void tExited(object p, EventArgs e)
            {
                tasksRunning = 0;
                for (var i = 0; i < tasks.Count; i++)
                {
                    if (tasks[i].id == task.id)
                    {
                        var t = tasks[i];
                        t.HasExited = true;
                        tasks[i] = t;
                    }
                    if (tasks[i].HasExited == false) tasksRunning++;
                }
                CheckAllExited();
            }

            task.Process.EnableRaisingEvents = true;
            task.Process.Start();
            task.Process.Exited += new EventHandler(tExited);
            task.Process.OutputDataReceived += new DataReceivedEventHandler(PrcsOutputHndlr);
            task.Process.ErrorDataReceived += new DataReceivedEventHandler(PrcsOutputHndlr);
            task.Process.BeginOutputReadLine();
            task.Process.BeginErrorReadLine();

            Console.WriteLine("Started FFMPEG task #" + task.id + " with arguments: " + arguments);

            tasks.Add(task);
            tasksRunning++;
            return true;
        }

        public void WaitForAllTasksFinished()
        {
            while(!allExited) System.Threading.Thread.Sleep(250);
        }
    }
}