using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

namespace VP9Booster
{
    class Program
    {
        struct FFMPEGInfo
        {
            public ulong numFrames { get; set; }
            public double FPS { get; set; }

            public override string ToString()
            {
                return "numFrames=" + numFrames + char.ConvertFromUtf32(10) + "FPS=" + FPS;
            }
        }

        struct Range
        {
            public double StartTS { get; set; }
            public double Length { get; set; }
        }

        private static void PrintHeader()
        {
            Console.WriteLine("VP9Booster - Runs several FFMPEG process instances to cope with the lack of USABLE multi threading of VP9");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: vp9booster :1 :2 :3 :4 :5");
            Console.WriteLine(" * 1: in file");
            Console.WriteLine(" * 2: ffmpeg options as a string");
            Console.WriteLine(" * 3: number of processes to spawn");
            Console.WriteLine(" * 4: ffmpeg options for merging");
            Console.WriteLine(" * 5: output file name");
        }

        private static bool GetInfo(string file, out FFMPEGInfo info)
        {
            var i_info = new FFMPEGInfo();
            Process ffmpegProc = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-i \"" + file + "\" -map 0:v:0 -c copy -f null -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            ffmpegProc.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            ffmpegProc.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);

            void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
            {
                string currentLine = outLine.Data;
                //Console.WriteLine(currentLine);
                if (currentLine != null)
                {
                    //Console.WriteLine("new Line:"+currentLine);
                    //Get FPS
                    int fpsIndex = currentLine.IndexOf(" fps, ");
                    string fpsCutout;
                    double fpsValue;
                    if (fpsIndex != -1)
                    {
                        fpsCutout = currentLine.Substring(0, fpsIndex);
                        fpsCutout = fpsCutout.Substring(fpsCutout.LastIndexOf(", ")+2);
                        bool doubleParseResult = double.TryParse(fpsCutout, NumberStyles.AllowDecimalPoint, CultureInfo.CreateSpecificCulture("en-US"), out fpsValue);
                        if (doubleParseResult)
                            i_info.FPS = fpsValue;
                    }

                    //Get total number of frames
                    string framesShelf = "";
                    ulong resultedFrames;
                    if (currentLine.StartsWith("frame="))
                    {
                        framesShelf = currentLine.Substring(6, currentLine.IndexOf("fps")-7).TrimStart();
                        bool longParseResult = ulong.TryParse(framesShelf, out resultedFrames);
                        if (longParseResult)
                            i_info.numFrames = resultedFrames;
                    }
                }
            }

            ffmpegProc.Start();
            ffmpegProc.BeginOutputReadLine();
            ffmpegProc.BeginErrorReadLine();
            ffmpegProc.WaitForExit();

            info = i_info;

            return true;
        }

        static int Main(string[] args)
        {
            /* Arguments are:
             * 0: in file
             * 1: ffmpeg options as a string " "
             * 2: number of "threads" to spawn
             * 3: ffmpeg options when merging
             * 4: output file
             */

            PrintHeader();

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.DefaultThreadCurrentCulture;

            if (args.LongLength != 5) { Console.WriteLine("Not enough arguments, need 5!"); goto error; }

            string inFile = args[0];
            string ffmpegOptions = args[1];
            ulong numProcesses; bool numProcessesOK = ulong.TryParse(args[2], out numProcesses);
            string mergeOptions = args[3];
            string outFile = args[4];

            if (!File.Exists(inFile)) { Console.WriteLine("1st parameter \"input file\" does not exist!"); goto error; }
            if (numProcesses < 2) { Console.WriteLine("3rd parameter \"number of processes\" must be greater than 1!"); goto error; }
            if (!numProcessesOK) {Console.WriteLine("3rd parameter \"number of processes\" could not be parsed as an integer!"); goto error;}
            if (outFile == "") { Console.WriteLine("4th parameter \"output file\" not provided!"); goto error; }

            FFMPEGInfo info;

            if (GetInfo(inFile, out info)) Console.WriteLine(info.ToString()); else { Console.WriteLine("Failed to get media info!"); goto error; }

            ulong pieceLength = info.numFrames / numProcesses;
            Console.WriteLine("pieceLength="+ pieceLength);
            List <Range> pieces = new List<Range>();
            ulong frameTS = 0, startTS= 0, endTS = pieceLength-1;

            for (ushort i =0;i<numProcesses;i++)
            {
                if(i==numProcesses-1) endTS += info.numFrames - endTS;
                double ffmpegStartTS = startTS / info.FPS, ffmpegEndTS = (endTS-startTS+1) / info.FPS;
                pieces.Add(new Range() { StartTS = ffmpegStartTS, Length = ffmpegEndTS });
                Console.WriteLine("Piece " + (i + 1) + ": startTS=" + startTS + ", endTS=" + endTS + ", length=" + (endTS-startTS+1) + ", ffmpegStartTS=" + ffmpegStartTS + ", length=" + ffmpegEndTS);
                frameTS += pieceLength;
                startTS = frameTS + 1;
                endTS = frameTS + pieceLength;
            }

            Console.WriteLine();

            bool IsShowingProgress = false;

            Stopwatch sElapsed = new Stopwatch();
            sElapsed.Start();

            FFMPEGTaskManager tm = new FFMPEGTaskManager();
            void ShowProgress(object sender, EventArgs e)
            {
                if (IsShowingProgress == false)
                {
                    IsShowingProgress = true;
                    Console.CursorLeft = 0;
                    Console.Write("{0:N2} percent done with {3:N2} fps, {1} out of {2} tasks running.               ", tm.Progress / (double)info.numFrames * 100d, tm.TasksRunnung, tm.TaskCount, tm.Progress / sElapsed.Elapsed.TotalSeconds);
                    Console.CursorLeft = 0;
                    IsShowingProgress = false;
                }
            }


            tm.OnProgressChanged += ShowProgress;
            ulong currentPart = 1;
            string mergeList = "";
            foreach (Range r in pieces)
            {
                string ffparams = "-i \"" + inFile + "\" -ss " + r.StartTS + " -t " + r.Length + " " + ffmpegOptions + " \"" + Path.GetDirectoryName(outFile) + "\\" + Path.GetFileNameWithoutExtension(outFile) + "_" + currentPart + Path.GetExtension(outFile) + "\"";
                mergeList += "file '" + Path.GetDirectoryName(outFile) + "\\" + Path.GetFileNameWithoutExtension(outFile) + "_" + currentPart + Path.GetExtension(outFile) + "'\n";
                tm.RunFFMPEGTask(ffparams);
                currentPart++;
            }
            tm.WaitForAllTasksFinished();
            sElapsed.Stop();

            Console.WriteLine("\nEncoding took " + sElapsed.Elapsed.ToString()+"\n");

            string mergeFileName = Path.GetDirectoryName(outFile) + "\\" + Path.GetFileNameWithoutExtension(outFile) + "_mergelist.txt";
            File.WriteAllText(mergeFileName, mergeList);

            mergeOptions = mergeOptions.Replace("$IN$", "\"" + inFile + "\"");

            IsShowingProgress = false;
            void mergeProgressUpdate(object sender, DataReceivedEventArgs e)
            {
                if (IsShowingProgress == false)
                {
                    IsShowingProgress = true;
                    if (e.Data.StartsWith("frame="))
                    {
                        string framesShelf = e.Data.Substring(6, e.Data.IndexOf(" ")).TrimStart();
                        ulong resultedFrames;
                        bool longParseResult = ulong.TryParse(framesShelf, out resultedFrames);
                        if (longParseResult)
                        {
                            Console.CursorLeft = 0;
                            Console.Write("{0:N2} percent done.          ", resultedFrames / (double)info.numFrames * 100d);
                            Console.CursorLeft = 0;
                        }
                        
                    }
                    IsShowingProgress = false;
                }
            }

            Process ffMerge = new Process() {
                StartInfo = new ProcessStartInfo() {
                    FileName = "ffmpeg"
                  , Arguments = "-f concat -safe 0 -i \"" + mergeFileName + "\" " + mergeOptions + " -c:v copy \"" + outFile + "\""
                  , UseShellExecute = false
                  , CreateNoWindow = true
                  , RedirectStandardOutput = true
                  , RedirectStandardError = true
                }
            };
            ffMerge.EnableRaisingEvents = true;
            ffMerge.Start();
            ffMerge.OutputDataReceived += mergeProgressUpdate;
            ffMerge.ErrorDataReceived += mergeProgressUpdate;
            Console.WriteLine("Started FFMPEG for merging with arguments: " + ffMerge.StartInfo.Arguments);
            ffMerge.WaitForExit();

            return 0;
            error:
            Console.WriteLine("Errors occurred!" + char.ConvertFromUtf32(10));
            PrintUsage();
            return 1;
        }
    }
}
