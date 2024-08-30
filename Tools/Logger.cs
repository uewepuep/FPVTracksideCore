using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Tools.Logger;

namespace Tools
{
    public class Logger : IDisposable
    {

        public static Logger UI { get; private set; }
        public static Logger TimingLog { get; private set; }
        public static Logger RaceLog { get; private set; }
        public static Logger SoundLog { get; private set; }
        public static Logger VideoLog { get; private set; }
        public static Logger Generation { get; private set; }
        public static Logger Input { get; private set; }
        public static Logger HTTP { get; private set; }
        public static Logger Sync { get; private set; }
        public static Logger Sheets { get; private set; }
        public static Logger OBS { get; private set; }
        public static Logger AutoRunner { get; private set; }
        public static Logger AllLog { get; private set; }

        public static CrashLogger CrashLogger { get; private set; }

        public static IEnumerable<Logger> Logs
        {
            get
            {
                yield return UI;
                yield return TimingLog;
                yield return RaceLog;
                yield return SoundLog;
                yield return VideoLog;
                yield return Generation;
                yield return Input;
                yield return HTTP;
                yield return Sync;
                yield return Sheets;
                yield return OBS;
                yield return AutoRunner;
                yield return AllLog;
            }
        }

        public static void Disable()
        {
            foreach (Logger log in Logs)
            {
                log.Enabled = false;
            }
        }

        public static void Init(DirectoryInfo logDir)
        {
            CleanUp();

            AllLog = new Logger(logDir, "Log");
            AllLog.WriteToConsole = true;

            UI = new Logger(logDir, "UILog", AllLog);
            TimingLog = new Logger(logDir, "TimingLog", AllLog);
            RaceLog = new Logger(logDir, "RaceLog", AllLog);
            SoundLog = new Logger(logDir, "SoundLog", AllLog);
            VideoLog = new Logger(logDir, "VideoLog", AllLog);
            Generation = new Logger(logDir, "Generation", AllLog);
            Input = new Logger(logDir, "Input", AllLog);
            HTTP = new Logger(logDir, "HTTP", AllLog);
            Sync = new Logger(logDir, "Sync", AllLog);
            Sheets = new Logger(logDir, "Sheets", AllLog);
            OBS = new Logger(logDir, "OBS", AllLog);
            AutoRunner = new Logger(logDir, "AutoRunner", AllLog);

            CrashLogger = new CrashLogger(logDir);

            AllLog.LogCall("\n\n\nLog Startup");
        }

        public static void CleanUp()
        {
            foreach (Logger log in Logs)
            {
                if (log != null)
                {
                    log.Dispose();
                }
            }
        }

        public enum LogType
        {
            Notice,
            Error,
            Exception
        }

        private List<LogItem> toWrite;

        private Version version;

        private Thread writeThread;
        private bool runWriteThread;
        private AutoResetEvent autoReset;

        private string filename;
        public string LogName { get; private set; }

        private Logger carbonCopy;

        public bool LogNotices { get; set; }

        private int counter;

        public bool Enabled { get; set; }

        public bool WriteToConsole { get; set; }

        public event Action<string> LogEvent;

        private List<LogItem> recentHistory;

        private object fileLocker;

        internal Logger(DirectoryInfo directory, string logName, Logger carbonCopy = null)
            :this(directory, logName, Assembly.GetEntryAssembly().GetName().Version, carbonCopy)
        {
            WriteToConsole = false;
            fileLocker = new object();
        }

        ~Logger()
        {
            Flush();
        }

        private Logger(DirectoryInfo directory, string logName, Version version, Logger carbonCopy)
        {
            counter = 0;
            LogName = logName;
            LogNotices = true;
            Enabled = true;

            this.carbonCopy = carbonCopy;
            filename =  Path.Combine(directory.FullName, logName + ".txt");
            this.version = version;

            if (carbonCopy != null)
            {
                // Don't need anything if we've got a CC
                return;
            }

            DoSizeCheck();

            recentHistory = new List<LogItem>();
            toWrite = new List<LogItem>();
            autoReset = new AutoResetEvent(false);
            runWriteThread = true;
            writeThread = new Thread(AutoFlush);
            writeThread.Name = "Log " + logName;
            writeThread.Start();
        }

        private void DoSizeCheck()
        {
            FileInfo fi = new FileInfo(filename);
            if (fi.Exists)
            {
                if (fi.Length > 1024 * 1024)
                {
                    int counter = 1;
                    while (File.Exists(fi.FullName + counter))
                    {
                        counter++;
                    }
                    fi.MoveTo(fi.FullName + counter);
                }
            }
        }

        public void Log(object caller, string message, object target = null, LogType type = LogType.Notice)
        {
            if (!Enabled)
                return;

            if (LogNotices || type != LogType.Notice)
            {
                LogItem logItem = new LogItem(LogName, caller, message, target, type, version);
                Enqueue(logItem);
            }
        }

        private void Enqueue(LogItem logItem)
        {
            if (!Enabled)
                return;

            if (carbonCopy != null)
            {
                carbonCopy.Enqueue(logItem);
            }
            else
            {
                lock (toWrite)
                {
                    toWrite.Add(logItem);
                    autoReset.Set();
                }

                lock (recentHistory)
                {
                    recentHistory.Add(logItem);

                    while (recentHistory.Count > 50)
                    {
                        recentHistory.RemoveAt(0);
                    }
                }

                LogEvent?.Invoke(logItem.ToString());
            }
        }

        public void LogCall(object caller, params object[] targets)
        {
            if (!Enabled)
                return;

            if (LogNotices)
            {
                StackFrame stackFrame = new StackFrame(1, false);
                Log(caller, stackFrame.GetMethod().Name, string.Join(", ", targets), LogType.Notice);
            }
        }

        public void LogStackTrace(object caller, params object[] targets)
        {
            if (!Enabled)
                return;

            StackTrace stackTrace = new StackTrace(true);
            Log(caller, stackTrace.ToString(), string.Join(", ", targets), LogType.Error);
        }

        public void LogException(object caller, Exception exception)
        {
            if (!Enabled)
                return;

            Log(caller, exception.ToString(), null, LogType.Exception);
        }

        public void FlushRecentHistory(Action<string> logEvents)
        {
            lock (recentHistory)
            {
                foreach (LogItem logItem in recentHistory)
                {
                    string str = logItem.ToString();
                    logEvents(str);
                }
            }
        }

        private void AutoFlush()
        {
            while (runWriteThread && Enabled)
            {
                if (autoReset.WaitOne(100))
                {
                    Flush();
                }
            }
        }

        public void Flush()
        {
            try
            {
                if (toWrite == null || toWrite.Count == 0)
                    return;

                if (counter > 1000)
                {
                    DoSizeCheck();
                    counter = 0;
                }

                LogItem[] cache;
                lock (toWrite)
                {
                    cache = toWrite.ToArray();
                    toWrite.Clear();
                }

                lock (fileLocker)
                {
                    using (StreamWriter stream = new StreamWriter(filename, true))
                    {
                        foreach (LogItem li in cache)
                        {
                            string line = li.ToString();

                            if (WriteToConsole)
                            {
                                Debug.WriteLine(line);
                                Console.WriteLine(line);
                            }
                            stream.WriteLine(line);

                            counter++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogException(this, e);
            }
        }

        public void Dispose()
        {
            if (runWriteThread)
            {
                runWriteThread = false;
                writeThread.Join();
                writeThread = null;
            }

            Flush();
        }

        public string GetContents(int length)
        {
            lock (fileLocker)
            {
                string contents = File.ReadAllText(filename);
                if (contents.Length > length)
                {
                    contents = contents.Substring(contents.Length - length, length);
                }
                return contents;
            }
        }
    }
    public class LogItem
    {
        public string LogName { get; private set; }
        public DateTime DateTime { get; private set; }

        public string Caller { get; private set; }
        public string Target { get; private set; }

        public string Message { get; private set; }
        public LogType Type { get; private set; }

        public string Version { get; private set; }

        public LogItem(string logName, object caller, string message, object target, LogType type, Version version)
        {
            LogName = logName;
            DateTime = DateTime.Now;

            if (caller != null)
            {
                Caller = caller.GetType().Name;
            }

            if (target != null)
            {
                Target = target.ToString();
            }

            Message = message;
            Type = type;
            Version = version.ToString();
        }

        public override string ToString()
        {
            string output = DateTime.ToString("yyyy/MM/dd HH:mm:ss.FFF") + " " + LogName + " (" + Type.ToString() + ")";
            if (Caller != null)
            {
                output += " - " + Caller;
            }

            if (!string.IsNullOrEmpty(Message))
            {
                output += ": " + Message;
            }

            if (!string.IsNullOrEmpty(Target))
            {
                output += " (" + Target + ")";
            }
            return output;
        }
    }

    public class CrashLogger
    {
        private FileInfo file;
        private Version logVersion;

        public CrashLogger(DirectoryInfo logDir)
        {
            file = new FileInfo(Path.Combine(logDir.FullName, "Crash.log"));

            logVersion = Assembly.GetEntryAssembly().GetName().Version;
        }

        public void Log(Exception ex)
        {
            LogItem logItem = new LogItem("Crash", "", ex.Message, ex, LogType.Exception, logVersion);

            using (StreamWriter stream = new StreamWriter(file.FullName, true))
            {
                string line = logItem.ToString();
                Debug.WriteLine(line);
                Console.WriteLine(line);
                stream.WriteLine(line);
            }
        }

        public void Clear()
        {
            file.Refresh();
            if (file.Exists)
            {
                file.Delete();
            }
        }

        public string GetContents()
        {
            file.Refresh();
            if (file.Exists)
            {
                return File.ReadAllText(file.FullName);
            }
            return "";
        }
    }

}
