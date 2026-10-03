// <copyright file="Debug.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows;

    public static class Debug
    {
        private static readonly string diagnosticsFolderPath;
        private static readonly Dictionary<int, DiagnosticsData> diagnosticsDataById = new Dictionary<int, DiagnosticsData>();
        private static readonly PropertyChangedEventArgs DataChangedEventArgs = new PropertyChangedEventArgs(nameof(Data));
        private static int threadCount = 0;
        private static readonly int mainThreadId = 0;
        private static int firstErrorCode;
        private static volatile bool isReleased;

        [ThreadStatic]
        private static DiagnosticsData currentDiagnosticsData;

        static Debug()
        {
            Debug.mainThreadId = Thread.CurrentThread.ManagedThreadId;

            string path = FileConverterExtension.PathHelpers.GetUserDataFolderPath;

            // 删除超过一天的诊断目录。
            DateTime expirationDate = DateTime.Now.Subtract(new TimeSpan(1, 0, 0, 0));
            string[] diagnosticsDirectories = Directory.GetDirectories(path, "Diagnostics-*");
            for (int index = 0; index < diagnosticsDirectories.Length; index++)
            {
                string directory = diagnosticsDirectories[index];
                DateTime creationTime = Directory.GetCreationTime(directory);
                if (creationTime < expirationDate)
                {
                    Directory.Delete(directory, true);
                }
            }

            string diagnosticsFolderName = $"Diagnostics-{DateTime.Now.Hour}h{DateTime.Now.Minute}m{DateTime.Now.Second}s";

            Debug.diagnosticsFolderPath = Path.Combine(path, diagnosticsFolderName);
            Debug.diagnosticsFolderPath = PathHelpers.GenerateUniquePath(Debug.diagnosticsFolderPath);
            Directory.CreateDirectory(Debug.diagnosticsFolderPath);

            Debug.Log($"诊断日志保存目录：'{Debug.diagnosticsFolderPath}'");
        }

        public static int FirstErrorCode => Volatile.Read(ref Debug.firstErrorCode);

        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

        public static DiagnosticsData[] Data
        {
            get
            {
                lock (Debug.diagnosticsDataById)
                {
                    return Debug.diagnosticsDataById.Values.ToArray();
                }
            }
        }

        public static void Log(string message)
        {
            Debug.LogInternal(error: false, message, ConsoleColor.White);
        }

        public static void Assert(bool condition)
        {
            if (!condition)
            {
                LogError("断言失败。");
            }
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                LogError(message);
            }
        }

        public static void LogError(string message)
        {
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);

            Debug.LogInternal(error: true, $"错误：{message}", ConsoleColor.Red);
        }

        public static void LogError(int errorCode, string message)
        {
            Interlocked.CompareExchange(ref Debug.firstErrorCode, errorCode, 0);

            Debug.LogError($"{message}（错误代码 0x{errorCode:X}）");
        }

        public static void Release()
        {
            Debug.Log("诊断日志管理器已释放。");

            DiagnosticsData[] data;
            lock (Debug.diagnosticsDataById)
            {
                if (Debug.isReleased)
                {
                    return;
                }

                Debug.isReleased = true;
                data = Debug.diagnosticsDataById.Values.ToArray();
                Debug.diagnosticsDataById.Clear();
            }

            foreach (DiagnosticsData diagnosticsData in data)
            {
                lock (diagnosticsData)
                {
                    diagnosticsData.Release();
                }
            }

            Debug.currentDiagnosticsData = null;
        }

        private static void LogInternal(bool error, string log, ConsoleColor color)
        {
            if (Debug.isReleased)
            {
                return;
            }

            DiagnosticsData diagnosticsData = Debug.currentDiagnosticsData;

            Thread currentThread = Thread.CurrentThread;
            int threadId = currentThread.ManagedThreadId;

            // 主线程日志同时显示在标准输出中。
            if (threadId == Debug.mainThreadId)
            {
                Console.ForegroundColor = color;
                if (error)
                {
                    Console.Error.WriteLine(log);
                }
                else
                {
                    Console.WriteLine(log);
                }

                Console.ResetColor();
            }

            if (diagnosticsData == null)
            {
                bool dataAdded = false;
                lock (Debug.diagnosticsDataById)
                {
                    if (Debug.isReleased)
                    {
                        return;
                    }

                    if (!Debug.diagnosticsDataById.TryGetValue(threadId, out diagnosticsData))
                    {
                        string threadName = Debug.threadCount > 0 ? $"{currentThread.Name} ({Debug.threadCount})" : "应用程序";
                        diagnosticsData = new DiagnosticsData(threadName);
                        diagnosticsData.Initialize(Debug.diagnosticsFolderPath, threadId);
                        Debug.diagnosticsDataById.Add(threadId, diagnosticsData);
                        Debug.threadCount++;
                        dataAdded = true;
                    }

                    // 短生命周期线程可能重用托管线程编号，复用既有诊断对象。
                    Debug.currentDiagnosticsData = diagnosticsData;
                }

                // 外部事件在集合锁外触发，避免界面回调阻塞其他日志线程。
                if (dataAdded)
                {
                    StaticPropertyChanged?.Invoke(null, DataChangedEventArgs);
                }
            }

            lock (diagnosticsData)
            {
                if (!Debug.isReleased)
                {
                    diagnosticsData.Log(log);
                }
            }
        }
    }
}
