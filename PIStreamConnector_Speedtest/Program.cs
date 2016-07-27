using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using OSIsoft.AF;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]

namespace PIStreamConnector_Speedtest
{
    class Program
    {
        //Create loggers
        private static readonly log4net.ILog statsLog = log4net.LogManager.GetLogger("StatsLogger");
        private static readonly log4net.ILog outLog = log4net.LogManager.GetLogger("OutLogger");
        private static DateTime blocksST, blockET;
        private static DateTime totalST, totalET;
        private static TimeSpan blockDuration, totalDuration;
        private static Int64 totalCount;
        private static PIDataPipe piPipe;

        //private static void ProcessSnap(AFDataPipeEvent snap)
        //{
        //    //Can Add more output here later
        //    outLog.InfoFormat("{0},{1},{2}", snap.Value.PIPoint.Name, snap.Value.Timestamp, snap.Value.Value);
        //    //Console.WriteLine("Point: {0}; Timestamp: {2}; Value: {1}", snap.Value.PIPoint.Name, snap.Value.Value, snap.Value.Timestamp);
        //}

        static void Main(string[] args)
        {

            //INIT stuff
            int PiPipe_EventBlock = Properties.Settings.Default.PIDataPipe_MaxEventCountPerServer;
            //Read file with PI Points to use
            string filepath = Path.Combine(Properties.Settings.Default.PIPoints_filepath, Properties.Settings.Default.PIPoints_filename);
            List<String> tagnames = new List<string>(File.ReadAllLines(filepath));

            //log info
            statsLog.Info("PIStreamConnector - Speedtest");
            statsLog.Info("1 - Run Pipe read to measure consumption performance");
            statsLog.Info("2 - Run Pipe processing to measure throughput performance");
            statsLog.Info("--- Press ENTER to start, ENTER to progress from test 1 to 2 and to exit");
            statsLog.Info("Settings:");
            statsLog.InfoFormat("# reads per call to DataPipe: {0}", PiPipe_EventBlock);
            statsLog.Info("------------------------------------------------------------");


            //Connect
            PIServer piserver = new PIServers().DefaultPIServer;
            piserver.Connect();
            statsLog.InfoFormat("Connected to PIserver: {0}", piserver.Name);

            //build PiPoints list
            List<PIPoint> pts = new List<PIPoint>();
            for (int r = 0; r < tagnames.Count; r++)
            {
                PIPoint pt = PIPoint.FindPIPoint(piserver, tagnames[r]);
                if (pt != null) pts.Add(pt);
            }


            #region Loop 1 - Run Pipe read to measure consumption performance
            //reset total stats
            totalCount = 0;
            totalDuration = System.TimeSpan.Zero;
            //Setup pipe
            piPipe = new PIDataPipe(AFDataPipeType.Snapshot);
            piPipe.AddSignups(pts);
            statsLog.Info("--------------------------------------------------------------------------------");
            statsLog.InfoFormat("Created PIDataPipe for {0} PIPoints", pts.Count);


            //Loop until keypress
            statsLog.Info("START 1 - Run Pipe read to measure consumption performance");
            statsLog.Info("--------------------------------------------------------------------------------");
            totalST = DateTime.Now;
            do
            {
                while (!Console.KeyAvailable)
                {
                    //log start
                    blocksST = DateTime.Now;
                    //Load results from datapipe, block process 1000 events
                    AFListResults<PIPoint, AFDataPipeEvent> myResults = piPipe.GetUpdateEvents(PiPipe_EventBlock);

                    //block stats
                    blockET = DateTime.Now;
                    blockDuration = blockET - blocksST;
                    totalCount += myResults.Count;
                    statsLog.InfoFormat("Duration: {0}s; Events: {1}; Throughput: {2}events/minute", blockDuration.TotalSeconds, myResults.Count, myResults.Count / blockDuration.TotalMinutes);
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            //Total stats
            totalET = DateTime.Now;
            totalDuration = totalET - totalST;
            statsLog.Info("--------------------------------------------------------------------------------");
            statsLog.Info("END 1 - Run Pipe read to measure consumption performance");
            statsLog.InfoFormat("Duration: {0}mins; Events: {1}; Throughput: {2}events/minute", totalDuration.TotalMinutes, totalCount, totalCount / totalDuration.TotalMinutes);
            statsLog.Info("--------------------------------------------------------------------------------");
            piPipe.Close();
            piPipe.Dispose();

            //wait for Enter key
            do { while (!Console.KeyAvailable) { } } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            #endregion

            #region Loop 2 - Run Pipe processing to measure throughput performance        
            //reset total stats
            totalCount = 0;
            totalDuration = System.TimeSpan.Zero;

            //Setup pipe
            piPipe = new PIDataPipe(AFDataPipeType.Snapshot);
            piPipe.AddSignups(pts);
            statsLog.Info("--------------------------------------------------------------------------------");
            statsLog.InfoFormat("Created PIDataPipe for {0} PIPoints", pts.Count);


            //Loop until keypress
            statsLog.Info("START 2 - Run Pipe processing to measure throughput performance");
            statsLog.Info("--------------------------------------------------------------------------------");
            totalST = DateTime.Now;
            do
            {
                while (!Console.KeyAvailable)
                {
                    //log start
                    blocksST = DateTime.Now;
                    //Load results from datapipe, block process 1000 events
                    AFListResults<PIPoint, AFDataPipeEvent> myResults = piPipe.GetUpdateEvents(PiPipe_EventBlock);

                    if (myResults.Results.Count > 0)
                    {
                        foreach (AFDataPipeEvent snap in myResults.Results)
                            //Filter new events only
                            if (snap.Action == AFDataPipeAction.Add || snap.Action == AFDataPipeAction.Update)
                                outLog.InfoFormat("{0},{1},{2}", snap.Value.PIPoint.Name, snap.Value.Timestamp, snap.Value.Value);
                        //ProcessSnap(snap);
                    }
                    //block stats
                    blockET = DateTime.Now;
                    blockDuration = blockET - blocksST;
                    totalCount += myResults.Count;
                    statsLog.InfoFormat("Duration: {0}s; Events: {1}; Throughput: {2}events/minute", blockDuration.TotalSeconds, myResults.Count, myResults.Count / blockDuration.TotalMinutes);
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            //Total stats
            totalET = DateTime.Now;
            totalDuration = totalET - totalST;
            statsLog.Info("--------------------------------------------------------------------------------");
            statsLog.Info("END 2 - Run Pipe processing to measure throughput performance");
            statsLog.InfoFormat("Duration: {0}mins; Events: {1}; Throughput: {2}events/minute", totalDuration.TotalMinutes, totalCount, totalCount / totalDuration.TotalMinutes);
            statsLog.Info("--------------------------------------------------------------------------------");
            piPipe.Close();
            piPipe.Dispose();
            #endregion

            //wait for Enter key
            do { while (!Console.KeyAvailable) { } } while (Console.ReadKey(true).Key != ConsoleKey.Enter);

            //Before exit: force rollover... does not work... either customize log4net or write a dummy entry (newline) the next minute.
            // Or create own roll and use FileAppender.
            //log4net.Appender.RollingFileAppender.
        }
    }
}
