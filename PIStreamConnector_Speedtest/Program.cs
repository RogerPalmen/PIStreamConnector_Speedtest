using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using OSIsoft.AF;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

using NLog;

namespace PIStreamConnector_Speedtest
{
    class Program
    {
        //Create loggers
        private static Logger nlog_stats;
        private static Logger nlog_data;
        //Init
        private static DateTime blocksST, blockET;
        private static DateTime totalST, totalET;
        private static TimeSpan blockDuration, totalDuration;
        private static Int64 totalCount, blockCount;
        private static PIDataPipe piPipe;
        private static int piPipe_EventBlock, piPipe_MaxAsyncBlockSize;

        //Nested class for IObserver
        //PI DataPipe uses AFDataPipeEvents
        public class PIPipeReceiver : IObserver<AFDataPipeEvent>
        {
            private AFDataPipeType _dataPipeType;

            //Constructor
            public PIPipeReceiver(AFDataPipeType piDataPipeType)
            {
                _dataPipeType = piDataPipeType;
            }

            //Process Event
            public void OnNext(AFDataPipeEvent value)
            {
                //Filter new events only
                if (value.Action == AFDataPipeAction.Add || value.Action == AFDataPipeAction.Update)
                    nlog_data.Info("{0},{1},{2}", value.Value.PIPoint.Name, value.Value.Timestamp, value.Value.Value);
                //increment block count
                blockCount++;
            }

            //Log error            
            public void OnError(Exception error)
            {
                nlog_stats.Debug("PIPipeReceiver: Provider has sent an error");
                nlog_stats.Debug(error.Message);
                nlog_stats.Debug(error.StackTrace);
            }

            //Log end of block
            /// Notifies the observer that the provider has finished sending push-based notifications.  
            public void OnCompleted()
            {
                nlog_stats.Debug("PIPipeReceiver: Provider has terminated sending data");
            }
        }

        static void Main(string[] args)
        {
            //locals
            bool pipeHasMoreEvents;
            PIServer piserver;
            AFErrors<PIPoint> pipeErrors;
            bool disableProcessing;
            string PIServerName;

            try
            {
                //Read config
                piPipe_EventBlock = Properties.Settings.Default.PIDataPipe_blockSize;
                piPipe_MaxAsyncBlockSize = Properties.Settings.Default.PIDataPipe_maxAsyncBlockSize;
                disableProcessing = Properties.Settings.Default.DisableProcessing;
                PIServerName = Properties.Settings.Default.PIServerName;
                //Init DataLogger, use a NULL logger if no processing required
                if (disableProcessing)
                {
                    nlog_data = LogManager.GetLogger("null");
                } else
                {
                    nlog_data = LogManager.GetLogger("data");
                }
                nlog_stats = LogManager.GetLogger("stats");


                //log info
                nlog_stats.Info("---------- PIStreamConnector - Speedtest ----------");
                nlog_stats.Info("-- Settings:");
                nlog_stats.Info("-- # reads per call to DataPipe: {0}", piPipe_EventBlock);
                nlog_stats.Info("-- # events to process per async block: {0}", piPipe_MaxAsyncBlockSize);
                nlog_stats.Info("-- Processing Enabled: {0}", disableProcessing);
                nlog_stats.Info("---------- INIT ----------");


                //Connect
                piserver = PIServer.FindPIServer(PIServerName);
                piserver.Connect();
                nlog_stats.Info("Connected to PIserver: {0}", piserver.Name);

                //Read file with PI Points to use
                string filepath = Path.Combine(Properties.Settings.Default.PIPoints_filepath, Properties.Settings.Default.PIPoints_filename);
                List<String> tagnames = new List<string>(File.ReadAllLines(filepath));
                nlog_stats.Info("PIPointsFile: Read {0} lines", tagnames.Count);

                //build PiPoints list
                List<PIPoint> pts = new List<PIPoint>();
                for (int r = 0; r < tagnames.Count; r++)
                {
                    PIPoint pt;
                    PIPoint.TryFindPIPoint(piserver, tagnames[r],out pt);
                    if (pt != null) pts.Add(pt);
                }
                nlog_stats.Info("PiPointsFile: Resolved {0} PI Points out of PIPointsFile {1} lines", pts.Count, tagnames.Count);

                //reset total stats
                totalCount = 0;
                totalDuration = System.TimeSpan.Zero;

                //Setup pipe
                piPipe = new PIDataPipe(AFDataPipeType.Snapshot);
                piPipe.AddSignups(pts);
                //Register Observer
                piPipe.Subscribe(new PIPipeReceiver(AFDataPipeType.Snapshot));
                nlog_stats.Info("Created PIDataPipe for {0} PIPoints", pts.Count);


                //Loop until keypress
                nlog_stats.Info("---------- Press ENTER to start, ENTER to exit ----------");
                do { while (!Console.KeyAvailable) { } } while (Console.ReadKey(true).Key != ConsoleKey.Enter);
                //and Go!
                nlog_stats.Info("---------- START ----------");
                totalST = DateTime.Now;
                //Outer keypress loop
                do
                {
                    while (!Console.KeyAvailable)
                    {
                        //block log start
                        blocksST = DateTime.Now;
                        blockCount = 0;

                        //Get all Events in the pipe, using pipeHasMoreEvents
                        do
                        {

                            //Pull updates into the pipe
                            pipeErrors = piPipe.GetObserverEvents(piPipe_EventBlock, out pipeHasMoreEvents);

                            //Log if there were errors in the pipe, stub for future extension
                            if (pipeErrors != null)
                            {
                                nlog_stats.Warn("Errors in PIDataPipe", pipeErrors);
                                pipeErrors = null; //clear
                            }
                        } while ((pipeHasMoreEvents) && (blockCount < piPipe_MaxAsyncBlockSize));

                        //Calc block stats
                        blockET = DateTime.Now;
                        blockDuration = blockET - blocksST;
                        totalCount += blockCount;
                        //Log block stats
                        if (blockCount >= piPipe_MaxAsyncBlockSize) nlog_stats.Warn("Within {0} events PIDataPipe processing has not consumed all available events", blockCount);
                        if (blockCount > 0)
                        {
                            nlog_stats.Info("Loop Duration: {0}s; Events: {1}; Throughput: {2}events/minute", blockDuration.TotalSeconds, blockCount, blockCount / blockDuration.TotalMinutes);
                        } else
                        {
                            nlog_stats.Info("Loop Duration: {0}s; No data available", blockDuration.TotalSeconds, blockCount, blockCount / blockDuration.TotalMinutes);
                        }
                        //Reset block stats
                        blockCount = 0;

                        //Wait for configured ms. This has to be tuned by user: wait less if losing events, wait more if too much time spent on calling for updates
                        //also refactor to use timers, tasks or whatever..
                        //=====-----===== !!! TODO !!! =====-----=====
                        Thread.Sleep(100);

                    }
                } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

                //Total stats
                totalET = DateTime.Now;
                totalDuration = totalET - totalST;
                nlog_stats.Info("---------- END ----------");
                nlog_stats.Info("Totalized Statistics:");
                nlog_stats.Info("Duration: {0}mins; Events: {1}; Throughput: {2}events/minute", totalDuration.TotalMinutes, totalCount, totalCount / totalDuration.TotalMinutes);

                //Cleanup
                piPipe.Close();
                piPipe.Dispose();
                piserver.Disconnect();

            }
            catch (Exception ex)
            {
                nlog_stats.Error(ex, "An exception occurred");
            }

            //wait for Enter key
            do { while (!Console.KeyAvailable) { } } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            //Before exit: force rollover
            //Need to find a workaround for that...
        }
    }
}
