using SwitcherLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MediaUpload
{
    class MediaUpload
    {
        private static int Main(string[] args)
        {
            try
            {
                MediaUpload.ProcessArgs(args);
                return 0;
            }
            catch (SwitcherLibException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return -1;
            }
        }

        private static void Help()
        {
            ConsoleUtils.Version();
            Console.Out.WriteLine();
            Console.Out.WriteLine("Usage: mediaupload.exe [options] <hostname> <slot:filename> [slot:filename ...]");
            Console.Out.WriteLine("       mediaupload.exe [options] <hostname> <slot> <filename>  (legacy mode)");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Uploads images to a BlackMagic ATEM switcher");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Arguments:");
            Console.Out.WriteLine();
            Console.Out.WriteLine(" hostname        - The hostname or IP of the ATEM switcher");
            Console.Out.WriteLine(" slot:filename   - Slot number and filename pairs (e.g., 1:image.png 2:image2.png)");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Options:");
            Console.Out.WriteLine();
            Console.Out.WriteLine(" -h, --help      - This help message");
            Console.Out.WriteLine(" -d, --debug     - Debug output");
            Console.Out.WriteLine(" -v, --version   - Version information");
            Console.Out.WriteLine(" -s, --skip-tally - Skip tally check (upload immediately without checking if slot is on program)");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Image Format:");
            Console.Out.WriteLine();
            Console.Out.WriteLine("The image must be the same resolution as the switcher. Accepted formats are BMP, JPEG, GIF, PNG and TIFF. Alpha channels are supported.");
        }

        private static void ProcessArgs(string[] args)
        {
            IList<string> args1 = new List<string>();
            bool skipTally = false;
            
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "-h":
                    case "--help":
                    case "-?":
                    case "/?":
                    case "/h":
                    case "/help":
                        MediaUpload.Help();
                        return;

                    case "-v":
                    case "--version":
                    case "/v":
                    case "/version":
                        ConsoleUtils.Version();
                        return;

                    case "-d":
                    case "--debug":
                    case "/d":
                    case "/debug":
                        Log.CurrentLevel = Log.Level.Debug;
                        break;

                    case "-s":
                    case "--skip-tally":
                    case "/s":
                    case "/skip-tally":
                        skipTally = true;
                        break;

                    default:
                        args1.Add(args[index]);
                        break;
                }
            }
            
            if (args1.Count < 2)
            {
                MediaUpload.Help();
                throw new SwitcherLibException("Invalid arguments");
            }

            // Detect batch mode: if second arg contains ":", it's batch mode
            if (args1[1].Contains(":"))
            {
                MediaUpload.UploadBatch(args1, skipTally);
            }
            else
            {
                MediaUpload.UploadLegacy(args1, skipTally);
            }
        }

        private static void UploadBatch(IList<string> args, bool skipTally)
        {
            string hostname = args[0];
            Switcher switcher = new Switcher(hostname);
            Log.Debug(String.Format("Switcher: {0}", switcher.GetProductName()));
            Log.Debug(String.Format("Resolution: {0}x{1}", switcher.GetVideoWidth().ToString(), switcher.GetVideoHeight().ToString()));

            if (skipTally)
            {
                Log.Info("Tally checking disabled - uploading immediately");
            }

            int totalImages = args.Count - 1;
            int currentImage = 0;
            int maxWaitSeconds = 300; // 5 minute timeout per image

            // Process each slot:filename pair
            for (int i = 1; i < args.Count; i++)
            {
                currentImage++;
                string arg = args[i];
                int colonIndex = arg.IndexOf(':');
                
                if (colonIndex == -1)
                {
                    throw new SwitcherLibException(String.Format("Invalid format: {0}. Expected slot:filename", arg));
                }

                string slotStr = arg.Substring(0, colonIndex);
                string filename = arg.Substring(colonIndex + 1);
                int slot = MediaUpload.GetSlot(slotStr);

                Log.Info(String.Format("[{0}/{1}] Uploading to slot {2}: {3}", currentImage, totalImages, slot + 1, filename));

                // Wait for slot to be safe (unless skip-tally is set)
                if (!skipTally)
                {
                    DateTime startWait = DateTime.Now;
                    bool slotSafe = false;
                    bool loggedWaiting = false;
                    
                    while (!slotSafe)
                    {
                        slotSafe = switcher.IsSlotSafeToUpload(slot);
                        
                        if (!slotSafe)
                        {
                            double waitedSeconds = (DateTime.Now - startWait).TotalSeconds;
                            
                            if (waitedSeconds >= maxWaitSeconds)
                            {
                                throw new SwitcherLibException(String.Format("Timeout waiting for slot {0} to become available", slot + 1));
                            }
                            
                            if (!loggedWaiting)
                            {
                                Log.Info(String.Format("Slot {0} is on program, waiting...", slot + 1));
                                loggedWaiting = true;
                            }
                            Thread.Sleep(100);
                        }
                    }
                    
                    if (loggedWaiting)
                    {
                        Log.Info(String.Format("Slot {0} is now safe, uploading...", slot + 1));
                    }
                }

                Upload upload = new Upload(switcher, filename, slot);
                upload.Start();
                
                int lastProgress = -1;
                while (upload.InProgress())
                {
                    int currentProgress = upload.GetProgress();
                    if (currentProgress != lastProgress)
                    {
                        Log.Info(String.Format("Progress: {0}%", currentProgress.ToString()));
                        lastProgress = currentProgress;
                    }
                    Thread.Sleep(100);
                }
                Log.Info(String.Format("Progress: {0}%", upload.GetProgress().ToString()));
            }
            
            Log.Info(String.Format("Batch complete: {0} images uploaded", totalImages));
        }

        private static void UploadLegacy(IList<string> args, bool skipTally)
        {
            if (args.Count < 3)
            {
                MediaUpload.Help();
                throw new SwitcherLibException("Invalid arguments");
            }

            Switcher switcher = new Switcher(args[0]);
            int slot = MediaUpload.GetSlot(args[1]);
            Log.Debug(String.Format("Switcher: {0}", switcher.GetProductName()));
            Log.Debug(String.Format("Resolution: {0}x{1}", switcher.GetVideoWidth().ToString(), switcher.GetVideoHeight().ToString()));
            args.RemoveAt(0);
            args.RemoveAt(0);

            string filename = String.Join(" ", args);

            // Wait for slot to be safe (unless skip-tally is set)
            if (!skipTally)
            {
                int maxWaitSeconds = 300;
                DateTime startWait = DateTime.Now;
                bool slotSafe = false;
                bool loggedWaiting = false;
                
                while (!slotSafe)
                {
                    slotSafe = switcher.IsSlotSafeToUpload(slot);
                    
                    if (!slotSafe)
                    {
                        double waitedSeconds = (DateTime.Now - startWait).TotalSeconds;
                        
                        if (waitedSeconds >= maxWaitSeconds)
                        {
                            throw new SwitcherLibException(String.Format("Timeout waiting for slot {0} to become available", slot + 1));
                        }
                        
                        if (!loggedWaiting)
                        {
                            Log.Info(String.Format("Slot {0} is on program, waiting...", slot + 1));
                            loggedWaiting = true;
                        }
                        Thread.Sleep(100);
                    }
                }
                
                if (loggedWaiting)
                {
                    Log.Info(String.Format("Slot {0} is now safe, uploading...", slot + 1));
                }
            }

            Upload upload = new Upload(switcher, filename, slot);
            upload.Start();
            while (upload.InProgress())
            {
                Log.Info(String.Format("Progress: {0}%", upload.GetProgress().ToString()));
                Thread.Sleep(100);
            }
            Log.Info(String.Format("Progress: {0}%", upload.GetProgress().ToString()));
        }

        private static int GetSlot(string arg)
        {
            try
            {
                return Convert.ToInt32(arg) - 1;
            }
            catch (Exception ex)
            {
                throw new SwitcherLibException(String.Format("Invalid slot: {0}", arg), ex);
            }
        }
    }
}
