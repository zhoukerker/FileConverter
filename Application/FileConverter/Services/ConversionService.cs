// <copyright file="ConversionService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Threading;

    using CommunityToolkit.Mvvm.ComponentModel;

    using FileConverter.ConversionJobs;
    using FileConverter.Diagnostics;

    public class ConversionService : ObservableObject, IConversionService
    {
        private readonly List<ConversionJob> conversionJobs = new List<ConversionJob>();
        private readonly object conversionLock = new object();
        private readonly int numberOfConversionThread;
        private readonly ISettingsService settingsService;
        private bool isConverting;

        public ConversionService(ISettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            this.ConversionJobs = this.conversionJobs.AsReadOnly();
            this.numberOfConversionThread = settingsService.Settings.MaximumNumberOfSimultaneousConversions;
            if (this.numberOfConversionThread <= 0)
            {
                this.numberOfConversionThread = Math.Max(1, Environment.ProcessorCount / 2);
            }

            Debug.Log($"最大转换线程数：{this.numberOfConversionThread}");
        }

        public event EventHandler<ConversionJobsTerminatedEventArgs> ConversionJobsTerminated;

        public ReadOnlyCollection<ConversionJob> ConversionJobs { get; }

        public void RegisterConversionJob(ConversionJob conversionJob)
        {
            if (conversionJob == null)
            {
                throw new ArgumentNullException(nameof(conversionJob));
            }

            lock (this.conversionLock)
            {
                if (this.isConverting)
                {
                    throw new InvalidOperationException("转换进行中无法注册新的转换任务。");
                }

                this.conversionJobs.Add(conversionJob);
            }

            this.OnPropertyChanged(nameof(this.ConversionJobs));
        }

        public void ConvertFilesAsync()
        {
            lock (this.conversionLock)
            {
                if (this.isConverting)
                {
                    return;
                }

                this.isConverting = true;
            }

            try
            {
                Helpers.InstantiateThread("ConversionQueueThread", this.ConvertFiles).Start();
            }
            catch
            {
                lock (this.conversionLock)
                {
                    this.isConverting = false;
                }

                throw;
            }
        }

        private void ConvertFiles()
        {
            bool allConversionsSucceed = false;
            try
            {
                ConversionJob[] jobs;
                lock (this.conversionLock)
                {
                    jobs = this.conversionJobs.ToArray();
                }

                foreach (ConversionJob job in jobs)
                {
                    job.PrepareConversion();
                }

                using (ConversionQueue queue = new ConversionQueue(jobs, this.numberOfConversionThread))
                {
                    Thread[] workers = new Thread[queue.WorkerCount];
                    try
                    {
                        for (int index = 0; index < workers.Length; index++)
                        {
                            int workerIndex = index;
                            Thread worker = Helpers.InstantiateThread($"ConversionWorker-{index + 1}", () => this.ExecuteConversionJobs(queue, workerIndex));
                            worker.Start();
                            workers[index] = worker;
                        }
                    }
                    finally
                    {
                        // 终态通知必须等到取消和文件清理真正完成。
                        foreach (Thread worker in workers)
                        {
                            worker?.Join();
                        }
                    }

                    if (this.settingsService.Settings.CopyFilesInClipboardAfterConversion && queue.OutputFiles.Count > 0)
                    {
                        Thread clipboardThread = Helpers.InstantiateThread("CopyFilesToClipboardThread", this.CopyFilesToClipboard);
                        clipboardThread.SetApartmentState(ApartmentState.STA);
                        clipboardThread.Start(queue.OutputFiles);
                        clipboardThread.Join();
                    }
                }

                allConversionsSucceed = true;
                foreach (ConversionJob job in jobs)
                {
                    allConversionsSucceed &= job.State == ConversionState.Done;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"转换过程中发生错误：{exception}");
            }
            finally
            {
                lock (this.conversionLock)
                {
                    this.isConverting = false;
                }
            }

            this.ConversionJobsTerminated?.Invoke(this, new ConversionJobsTerminatedEventArgs(allConversionsSucceed));
        }

        private void ExecuteConversionJobs(ConversionQueue queue, int workerIndex)
        {
            ConversionJob conversionJob;
            while ((conversionJob = queue.GetNextJob(workerIndex)) != null)
            {
                try
                {
                    conversionJob.StartConversion();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"转换过程中发生错误：{exception}");
                }
                finally
                {
                    queue.CompleteJob(workerIndex);
                }
            }
        }

        private void CopyFilesToClipboard(object filePaths)
        {
            try
            {
                StringCollection paths = (StringCollection)filePaths;
                System.Windows.Forms.Clipboard.SetFileDropList(paths);
                Debug.Log("已将以下输出文件复制到剪贴板：");
                foreach (string path in paths)
                {
                    Debug.Log($"  {path}");
                }
            }
            catch (Exception exception)
            {
                Debug.Log($"无法将文件复制到剪贴板：{exception}");
            }
        }

        private sealed class ConversionQueue : IDisposable
        {
            private readonly object syncRoot = new object();
            private readonly ConversionJob[] pendingJobs;
            private readonly ConversionJob[] activeJobs;
            private readonly HashSet<string> outputFilePaths = new HashSet<string>(StringComparer.Ordinal);
            private int nextPendingJobIndex;
            private int remainingJobsCount;

            public ConversionQueue(ConversionJob[] jobs, int workerCount)
            {
                this.pendingJobs = (ConversionJob[])jobs.Clone();
                for (int index = 0; index < this.pendingJobs.Length; index++)
                {
                    ConversionJob job = this.pendingJobs[index];
                    if (job.State == ConversionState.Ready)
                    {
                        this.remainingJobsCount++;
                        job.PropertyChanged += this.ConversionJob_PropertyChanged;
                    }
                    else
                    {
                        this.pendingJobs[index] = null;
                    }
                }

                this.activeJobs = new ConversionJob[Math.Min(workerCount, this.remainingJobsCount)];
            }

            public int WorkerCount => this.activeJobs.Length;

            public StringCollection OutputFiles { get; } = new StringCollection();

            public ConversionJob GetNextJob(int workerIndex)
            {
                lock (this.syncRoot)
                {
                    while (this.remainingJobsCount > 0)
                    {
                        ConversionFlags flags = ConversionFlags.None;
                        foreach (ConversionJob activeJob in this.activeJobs)
                        {
                            if (activeJob != null)
                            {
                                // 预留任务也参与互斥，避免线程尚未启动时重复占用光驱。
                                flags |= activeJob.StateFlags;
                            }
                        }

                        for (int index = this.nextPendingJobIndex; index < this.pendingJobs.Length; index++)
                        {
                            ConversionJob job = this.pendingJobs[index];
                            if (job == null || !job.CanStartConversion(flags))
                            {
                                continue;
                            }

                            this.pendingJobs[index] = null;
                            this.activeJobs[workerIndex] = job;
                            this.remainingJobsCount--;
                            while (this.nextPendingJobIndex < this.pendingJobs.Length && this.pendingJobs[this.nextPendingJobIndex] == null)
                            {
                                this.nextPendingJobIndex++;
                            }

                            if (this.outputFilePaths.Add(job.OutputFilePath))
                            {
                                this.OutputFiles.Add(job.OutputFilePath);
                            }

                            return job;
                        }

                        Monitor.Wait(this.syncRoot);
                    }

                    return null;
                }
            }

            public void CompleteJob(int workerIndex)
            {
                lock (this.syncRoot)
                {
                    ConversionJob job = this.activeJobs[workerIndex];
                    job.PropertyChanged -= this.ConversionJob_PropertyChanged;
                    this.activeJobs[workerIndex] = null;
                    Monitor.PulseAll(this.syncRoot);
                }
            }

            public void Dispose()
            {
                foreach (ConversionJob job in this.pendingJobs)
                {
                    if (job != null)
                    {
                        job.PropertyChanged -= this.ConversionJob_PropertyChanged;
                    }
                }
            }

            private void ConversionJob_PropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.PropertyName == nameof(ConversionJob.StateFlags))
                {
                    lock (this.syncRoot)
                    {
                        Monitor.PulseAll(this.syncRoot);
                    }
                }
            }
        }
    }
}
