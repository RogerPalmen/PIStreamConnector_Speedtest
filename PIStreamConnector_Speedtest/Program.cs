using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using OSIsoft.AF;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

using NLog;
//[assembly: log4net.Config.XmlConfigurator(Watch = true)]

namespace PIStreamConnector_Speedtest
{
    class Program
    {
        //Create loggers
        //private static readonly log4net.ILog statsLog = log4net.LogManager.GetLogger("StatsLogger");
        //private static readonly log4net.ILog outLog = log4net.LogManager.GetLogger("OutLogger");
        private static Logger nlog_stats = LogManager.GetLogger("stats");
        private static Logger nlog_data = LogManager.GetLogger("data");
        //Init
        private static DateTime blocksST, blockET;
        private static DateTime totalST, totalET;
        private static TimeSpan blockDuration, totalDuration;
        private static Int64 totalCount;
        private static PIDataPipe piPipe;

        //PI DataPipe uses AFDataPipeEvents
        public class PIPipeReceiver : IObserver<AFDataPipeEvent>
        {
            private AFDataPipeType _dataPipeType;

            //Constructor
            public PIPipeReceiver(AFDataPipeType afDataPipeType)
            {
                _dataPipeType = afDataPipeType;
            }

            //Process Event
            public void OnNext(AFDataPipeEvent value)
            {
                //Filter new events only
                if (value.Action == AFDataPipeAction.Add || value.Action == AFDataPipeAction.Update)
                    nlog_data.Info("{0},{1},{2}", value.Value.PIPoint.Name, value.Value.Timestamp, value.Value.Value);
            }

            //Log error            
            public void OnError(Exception error)
            {
                nlog_stats.Info("Provider has sent an error");
                nlog_stats.Info(error.Message);
                nlog_stats.Info(error.StackTrace);
            }

            //Log end of block
            /// Notifies the observer that the provider has finished sending push-based notifications.  
            public void OnCompleted()
            {
                nlog_stats.Info("Provider has terminated sending data");
            }
        }

        static void Main(string[] args)
        {

            //locals
            bool pipeHasMoreEvents;
            //INIT stuff
            int PiPipe_EventBlock = Properties.Settings.Default.PIDataPipe_MaxEventCountPerServer;
            bool disableProcessing = Properties.Settings.Default.DisableProcessing;
            string PIServerName = Properties.Settings.Default.PIServerName;
            //Read file with PI Points to use
            string filepath = Path.Combine(Properties.Settings.Default.PIPoints_filepath, Properties.Settings.Default.PIPoints_filename);
            List<String> tagnames = new List<string>(File.ReadAllLines(filepath));

            //log info
            nlog_stats.Info("---------- PIStreamConnector - Speedtest ----------");
            nlog_stats.Info("-- Settings:");
            nlog_stats.Info("-- # reads per call to DataPipe: {0}", PiPipe_EventBlock);
            nlog_stats.Info("-- Processing Enabled: {0}", disableProcessing);
            nlog_stats.Info("---------- INIT ----------");


            //Connect
            PIServer piserver = PIServer.FindPIServer(PIServerName);
            piserver.Connect();
            nlog_stats.Info("Connected to PIserver: {0}", piserver.Name);

            //build PiPoints list
            List<PIPoint> pts = new List<PIPoint>();
            for (int r = 0; r < tagnames.Count; r++)
            {
                PIPoint pt = PIPoint.FindPIPoint(piserver, tagnames[r]);
                if (pt != null) pts.Add(pt);
            }
  
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
            nlog_stats.Info("---------- START ----------");
            totalST = DateTime.Now;
            do
            {
                while (!Console.KeyAvailable)
                {
                    //log start
                    blocksST = DateTime.Now;
                    //Load results from datapipe, block process X events
                    //disable: use observer AFListResults<PIPoint, AFDataPipeEvent> myResults = piPipe.GetUpdateEvents(PiPipe_EventBlock);
                    piPipe.GetObserverEvents(PiPipe_EventBlock, out pipeHasMoreEvents);

                    //if (myResults.Results.Count > 0)
                    //{
                    //    foreach (AFDataPipeEvent snap in myResults.Results)
                    //        //Only if processing enabled
                    //        if (!disableProcessing)
                    //        {
                    //            //Filter new events only
                    //            if (snap.Action == AFDataPipeAction.Add || snap.Action == AFDataPipeAction.Update)
                    //                nlog_data.Info("{0},{1},{2}", snap.Value.PIPoint.Name, snap.Value.Timestamp, snap.Value.Value);
                    //                //nlog_data.Info("{0},{1},{2}", "PIPoint.Name", "Timestamp", "Value"); //Test dummy values
                    //                //nlog_data.Info("PIPoint.Name, Timestamp, Value"); //Test non-formatted values
                    //        }
                    //}
                    //block stats
                    blockET = DateTime.Now;
                    blockDuration = blockET - blocksST;
                    //totalCount += myResults.Count;
                    //nlog_stats.Info("Loop Duration: {0}s; Events: {1}; Throughput: {2}events/minute", blockDuration.TotalSeconds, myResults.Count, myResults.Count / blockDuration.TotalMinutes);
                    nlog_stats.Info("Loop Duration: {0}s; Block: {1}; HasMoreEvents: {2}", blockDuration.TotalSeconds, PiPipe_EventBlock, pipeHasMoreEvents);
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

            //wait for Enter key
            do { while (!Console.KeyAvailable) { } } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            //Before exit: force rollover
            //TODO!
        }
    }
}
