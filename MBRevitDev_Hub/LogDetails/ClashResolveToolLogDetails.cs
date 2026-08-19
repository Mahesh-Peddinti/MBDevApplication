using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.LogDetails
{
    public class ClashResolveToolLogDetails
    {
        
        private static readonly string LogFolder =
                        Path.Combine((@"D:\Develpments\MBRevitDev_Hub"),
                                          "ClashResolveToolLogDetails");

        private static readonly string LogFile =
            Path.Combine(LogFolder,
                         $"RevitLog_{DateTime.Now:yyyyMMdd}.txt");

        static ClashResolveToolLogDetails()
        {
            if (!Directory.Exists(LogFolder))
                Directory.CreateDirectory(LogFolder);
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warning(string message)
        {
            WriteLog("WARNING", message);
        }

        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        public static void Error(Exception ex)
        {
            WriteLog("ERROR", ex.ToString());
        }

        private static void WriteLog(string level, string message)
        {
            string logEntry =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

            File.AppendAllText(LogFile,
                logEntry + Environment.NewLine);
        }


    }


}
  
