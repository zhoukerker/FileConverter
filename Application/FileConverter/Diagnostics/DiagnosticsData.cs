// <copyright file="DiagnosticsData.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;

    using FileConverter.Annotations;

    public class DiagnosticsData : INotifyPropertyChanged
    {
        private readonly List<string> logMessages = new List<string>();
        private readonly StringBuilder stringBuilder = new StringBuilder();
        private readonly object syncRoot = new object();
        private Timer flushTimer;
        private string cachedContent;
        private System.IO.TextWriter logFileWriter;
        private string name;

        public DiagnosticsData(string name)
        {
            this.Name = name;
            this.LogMessages = new ReadOnlyCollection<string>(this.logMessages);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => this.name;

            private set
            {
                this.name = value;
                this.OnPropertyChanged();
            }
        }

        public string Content
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.cachedContent == null)
                    {
                        this.stringBuilder.Clear();
                        for (int index = 0; index < this.logMessages.Count; index++)
                        {
                            this.stringBuilder.AppendLine(this.logMessages[index]);
                        }

                        this.cachedContent = this.stringBuilder.ToString();
                    }

                    return this.cachedContent;
                }
            }
        }

        public ReadOnlyCollection<string> LogMessages
        {
            get;
            private set;
        }

        public void Initialize(string diagnosticsFolderPath, int id)
        {
            string path = Path.Combine(diagnosticsFolderPath, $"Diagnostics{id}.log");
            path = PathHelpers.GenerateUniquePath(path);
            this.logFileWriter = new StreamWriter(File.Open(path, FileMode.Create));
            // 定时刷新和退出刷新兼顾日志可读性与磁盘写入开销。
            this.flushTimer = new Timer(this.Flush, null, 1000, 1000);

            this.Log($"{System.DateTime.Now.ToLongDateString()} {System.DateTime.Now.ToLongTimeString()}\n");
        }

        public void Release()
        {
            lock (this.syncRoot)
            {
                this.flushTimer?.Dispose();
                this.flushTimer = null;
                this.logFileWriter?.Dispose();
                this.logFileWriter = null;
            }
        }

        public void Log(string log)
        {
            lock (this.syncRoot)
            {
                this.logMessages.Add(log);
                this.cachedContent = null;
                this.logFileWriter?.WriteLine(log);
            }

            this.OnPropertyChanged(nameof(this.Content));
        }

        private void Flush(object state)
        {
            lock (this.syncRoot)
            {
                try
                {
                    this.logFileWriter?.Flush();
                }
                catch (IOException)
                {
                    // 后台刷新失败时保留缓冲，交由后续写入或退出时处理。
                }
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
