// <copyright file="ConversionJob.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Windows.Input;

    using CommunityToolkit.Mvvm.Input;

    using FileConverter.Diagnostics;
    using Microsoft.Win32.SafeHandles;

    public class ConversionJob : INotifyPropertyChanged
    {
        private static readonly ConcurrentDictionary<string, PropertyChangedEventArgs> PropertyChangedEventArgsCache = new ConcurrentDictionary<string, PropertyChangedEventArgs>();

        private float progress = 0f;
        private DateTime startTime;
        private ConversionState state = ConversionState.Unknown;
        private ConversionFlags stateFlags;
        private string errorMessage = string.Empty;
        private string userState = string.Empty;
        private RelayCommand cancelCommand;
        private volatile bool cancelIsRequested;

        private readonly string initialInputPath;
        private int currentOutputFilePathIndex;
        private Dictionary<string, OutputFileSnapshot> preexistingOutputFiles;

        public ConversionJob()
        {
            this.UserState = "Design Mode";

            this.ConversionPreset = null;
            this.initialInputPath = string.Empty;
            this.InputFilePath = @"C:\Path\To\AVery\Long\Location\WithAVeryNiceFile.png";
            this.OutputFilePaths = new[] { "C:\\Path\\To\\AVery\\Long\\Location\\WithAVeryNiceFile.jpg" };
            this.StartTime = DateTime.Now - TimeSpan.FromMinutes(1.2);
            this.State = ConversionState.InProgress;
            this.StateFlags = ConversionFlags.None;
            this.Progress = 0.6f;
        }

        public ConversionJob(ConversionPreset conversionPreset, string inputFilePath)
        {
            if (conversionPreset == null)
            {
                throw new ArgumentNullException(nameof(conversionPreset));
            }

            if (string.IsNullOrEmpty(inputFilePath))
            {
                throw new ArgumentNullException(nameof(inputFilePath));
            }

            this.State = ConversionState.Unknown;
            this.initialInputPath = inputFilePath;
            this.InputFilePath = inputFilePath;
            this.ConversionPreset = conversionPreset;
            this.UserState = Properties.Resources.ConversionStatePrepareConversion;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ConversionPreset ConversionPreset
        {
            get;
            private set;
        }

        public string InputFilePath
        {
            get;
            set;
        }

        public string OutputFilePath
        {
            get
            {
                if (this.OutputFilePaths == null || this.OutputFilePaths.Length == 0)
                {
                    return string.Empty;
                }

                if (this.CurrentOutputFilePathIndex < 0)
                {
                    return this.OutputFilePaths[0];
                }

                if (this.CurrentOutputFilePathIndex >= this.OutputFilePaths.Length)
                {
                    return this.OutputFilePaths[this.OutputFilePaths.Length - 1];
                }

                return this.OutputFilePaths[this.CurrentOutputFilePathIndex];
            }
        }

        public ConversionState State
        {
            get => this.state;

            private set
            {
                if (this.state == value)
                {
                    return;
                }

                this.state = value;
                this.NotifyPropertyChanged();
                if (this.cancelCommand != null)
                {
                    Application.Current.Dispatcher.Invoke(this.cancelCommand.NotifyCanExecuteChanged);
                }
            }
        }

        public string UserState
        {
            get => this.userState;

            protected set
            {
                if (this.userState == value)
                {
                    return;
                }

                this.userState = value;
                this.NotifyPropertyChanged();
            }
        }

        public float Progress
        {
            get => this.progress;

            protected set
            {
                if (this.progress == value)
                {
                    return;
                }

                this.progress = value;
                this.NotifyPropertyChanged();
            }
        }

        public DateTime StartTime
        {
            get => this.startTime;

            protected set
            {
                this.startTime = value;
                this.NotifyPropertyChanged();
            }
        }

        public string ErrorMessage
        {
            get => this.errorMessage;

            private set
            {
                this.errorMessage = value;
                this.NotifyPropertyChanged();
            }
        }

        public ConversionFlags StateFlags
        {
            get => this.stateFlags;

            protected set
            {
                if (this.stateFlags == value)
                {
                    return;
                }

                this.stateFlags = value;
                this.NotifyPropertyChanged();
            }
        }

        public ICommand CancelCommand
        {
            get
            {
                if (this.cancelCommand == null)
                {
                    this.cancelCommand = new RelayCommand(this.Cancel, this.IsCancelable);
                }

                return this.cancelCommand;
            }
        }

        protected bool CancelIsRequested
        {
            get => this.cancelIsRequested;
            private set => this.cancelIsRequested = value;
        }

        protected int CurrentOutputFilePathIndex
        {
            get => this.currentOutputFilePathIndex;

            set
            {
                this.currentOutputFilePathIndex = value;
                this.NotifyPropertyChanged(nameof(this.OutputFilePath));
            }
        }

        protected virtual InputPostConversionAction InputPostConversionAction
        {
            get
            {
                if (this.ConversionPreset == null)
                {
                    return InputPostConversionAction.None;
                }

                return this.ConversionPreset.InputPostConversionAction;
            }
        }

        protected virtual bool IsCancelable() => this.State == ConversionState.InProgress;

        protected string[] OutputFilePaths
        {
            get;
            private set;
        }

        public virtual bool CanStartConversion(ConversionFlags conversionFlags)
        {
            return (conversionFlags & ConversionFlags.CdDriveExtraction) == 0;
        }

        public void PrepareConversion(params string[] outputFilePaths)
        {
            try
            {
                this.PrepareConversionCore(outputFilePaths);
            }
            catch (Exception exception)
            {
                this.ConversionFailed(Properties.Resources.ErrorDuringJobInitialization);
                Debug.Log(exception.ToString());
            }
            finally
            {
                if (this.State == ConversionState.Failed)
                {
                    this.ReleaseResourcesSafely();
                }
            }
        }

        private void PrepareConversionCore(string[] outputFilePaths)
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            this.InputFilePath = this.initialInputPath;

            string extension = System.IO.Path.GetExtension(this.initialInputPath);
            extension = extension.Length > 0 ? extension.Substring(1) : string.Empty;
            string extensionCategory = Helpers.GetExtensionCategory(extension);
            if (!Helpers.IsOutputTypeCompatibleWithCategory(this.ConversionPreset.OutputType, extensionCategory))
            {
                this.ConversionFailed(Properties.Resources.ErrorInputTypeIncompatibleWithOutputType);
                return;
            }

            this.OutputFilePaths = outputFilePaths ?? new string[0];
            this.preexistingOutputFiles = null;
            if (this.OutputFilePaths.Length == 0)
            {
                int outputFilesCount = this.GetOutputFilesCount();
                this.OutputFilePaths = new string[outputFilesCount];
            }

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                if (!string.IsNullOrEmpty(this.OutputFilePaths[index]))
                {
                    // 显式指定的已有文件不属于本任务，失败清理时保留它们。
                    if (System.IO.File.Exists(this.OutputFilePaths[index]))
                    {
                        if (this.preexistingOutputFiles == null)
                        {
                            this.preexistingOutputFiles = new Dictionary<string, OutputFileSnapshot>(StringComparer.OrdinalIgnoreCase);
                        }

                        this.preexistingOutputFiles[this.OutputFilePaths[index]] = CaptureOutputFile(this.OutputFilePaths[index]);
                    }

                    continue;
                }

                string path = this.ConversionPreset.GenerateOutputFilePath(this.initialInputPath, index + 1, this.OutputFilePaths.Length);

                if (!PathHelpers.IsPathValid(path))
                {
                    this.ConversionFailed(Properties.Resources.ErrorInvalidOutputPath);
                    Debug.Log($"生成的输出路径无效：{path}；输入路径：{this.InputFilePath}。");
                    return;
                }

                if (path == this.InputFilePath)
                {
                    // 需要移动或删除源文件时先重命名源文件，保留目标文件名。
                    if (this.ConversionPreset.InputPostConversionAction == InputPostConversionAction.MoveInArchiveFolder ||
                        this.ConversionPreset.InputPostConversionAction == InputPostConversionAction.Delete)
                    {
                        string inputExtension = System.IO.Path.GetExtension(this.InputFilePath);
                        string pathWithoutExtension = this.InputFilePath.Substring(0, this.InputFilePath.Length - inputExtension.Length);
                        this.InputFilePath = PathHelpers.GenerateUniquePath(pathWithoutExtension + "_TEMP" + inputExtension);
                        System.IO.File.Move(this.initialInputPath, this.InputFilePath);
                    }
                }

                // 创建尚不存在的输出目录。
                if (!PathHelpers.CreateFolders(path))
                {
                    this.ConversionFailed(Properties.Resources.ErrorFailToCreateOutputPathFolders);
                    return;
                }

                // 规范化输出路径。
                try
                {
                    path = PathHelpers.GenerateUniquePath(path, this.OutputFilePaths);
                }
                catch (Exception exception)
                {
                    this.ConversionFailed(Properties.Resources.ErrorFailToGenerateUniqueOutputPath);
                    Debug.Log(exception.Message);
                    return;
                }

                this.OutputFilePaths[index] = path;
            }

            this.CurrentOutputFilePathIndex = 0;

            // 检查输入文件是否位于光驱。
            if (PathHelpers.IsOnCDDrive(this.InputFilePath))
            {
                this.StateFlags = ConversionFlags.CdDriveExtraction;
            }

            try
            {
                this.Initialize();
            }
            catch (Exception exception)
            {
                this.ConversionFailed(Properties.Resources.ErrorDuringJobInitialization);
                Debug.Log(exception.ToString());
                return;
            }

            if (this.State == ConversionState.Unknown)
            {
                this.State = ConversionState.Ready;
            }

            Debug.Log($"转换任务已初始化；预设：'{this.ConversionPreset.FullName}'；输入：{this.InputFilePath}；输出：{this.OutputFilePath}");

            if (this.State != ConversionState.Failed)
            {
                this.UserState = Properties.Resources.ConversionStateInQueue;
            }
        }

        public void StartConversion()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            if (this.State != ConversionState.Ready)
            {
                throw new Exception("转换任务状态无效。");
            }

            Debug.Log($"转换文件：{this.InputFilePath} → {this.OutputFilePath}。");

            this.StartTime = DateTime.Now;
            this.State = ConversionState.InProgress;

            try
            {
                this.Convert();
            }
            catch (Exception exception)
            {
                this.ConversionFailed(exception.Message);
            }
            finally
            {
                this.ReleaseResourcesSafely();
                this.StateFlags = ConversionFlags.None;
            }

            // 确认输出完整后再处理源文件，避免编码器未生成结果时删除原始文件。
            if (this.State != ConversionState.Failed && !this.AllOutputFilesExists())
            {
                this.ConversionFailed(Properties.Resources.ErrorCantFindOutputFiles);
            }

            try
            {
                if (this.State == ConversionState.Failed)
                {
                    this.OnConversionFailed();
                }
                else
                {
                    this.OnConversionSucceed();
                }
            }
            catch (Exception exception)
            {
                this.ConversionFailed(exception.Message);
                Debug.Log(exception.ToString());
            }

            if (this.State == ConversionState.Failed && this.AtLeastOneOutputFilesExists())
            {
                Debug.Log(Properties.Resources.ErrorConversionFailedWithOutput);
            }
        }

        public virtual void Cancel()
        {
            if (!this.IsCancelable())
            {
                return;
            }

            this.CancelIsRequested = true;
            this.ConversionFailed(Properties.Resources.ErrorCanceled);
        }

        protected virtual int GetOutputFilesCount()
        {
            return 1;
        }

        protected virtual void Convert()
        {
        }

        protected virtual void Initialize()
        {
        }

        protected virtual void ReleaseResources()
        {
        }

        private void ReleaseResourcesSafely()
        {
            try
            {
                this.ReleaseResources();
            }
            catch (Exception exception)
            {
                this.ConversionFailed(exception.Message);
                Debug.Log(exception.ToString());
            }
        }

        protected bool StartChildConversion(ConversionJob conversionJob, bool updateUserState = true)
        {
            if (this.CancelIsRequested || this.State == ConversionState.Failed)
            {
                return false;
            }

            if (conversionJob.State == ConversionState.Failed)
            {
                this.ConversionFailed(conversionJob.ErrorMessage);
                return false;
            }

            // 直接转发子任务的进度，避免定时轮询和额外工作线程。
            PropertyChangedEventHandler progressChanged = (sender, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(this.Progress))
                {
                    this.Progress = conversionJob.Progress;
                }
                else if (updateUserState && eventArgs.PropertyName == nameof(this.UserState))
                {
                    this.UserState = conversionJob.UserState;
                }
            };

            conversionJob.PropertyChanged += progressChanged;
            try
            {
                conversionJob.StartConversion();
            }
            finally
            {
                conversionJob.PropertyChanged -= progressChanged;
            }

            if (conversionJob.State != ConversionState.Done)
            {
                this.ConversionFailed(conversionJob.ErrorMessage);
                return false;
            }

            return !this.CancelIsRequested && this.State != ConversionState.Failed;
        }

        protected static void DeleteIntermediateFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            try
            {
                System.IO.File.Delete(filePath);
            }
            catch (Exception exception)
            {
                // 清理失败只记录日志，保留转换过程中产生的原始错误。
                Debug.Log($"无法删除临时中间文件 '{filePath}'：{exception}。");
            }
        }

        protected virtual void OnConversionFailed()
        {
            Debug.Log("转换失败。");

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                try
                {
                    if (string.IsNullOrEmpty(outputFilePath) ||
                        this.preexistingOutputFiles?.ContainsKey(outputFilePath) == true ||
                        string.Equals(System.IO.Path.GetFullPath(outputFilePath), System.IO.Path.GetFullPath(this.InputFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (System.IO.File.Exists(outputFilePath))
                    {
                        System.IO.File.Delete(outputFilePath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Log($"转换失败后无法删除文件：'{outputFilePath}'。");
                    Debug.Log($"发生异常：{exception}。");
                }
            }
        }

        protected virtual void OnConversionSucceed()
        {
            Debug.Log("转换成功。");

            this.ChangeOutputFileTimestampToMatchOriginal();

            InputPostConversionAction postConversionAction = this.InputPostConversionAction;
            if (postConversionAction != InputPostConversionAction.None && this.InputIsOutputFile())
            {
                // 原地写入的结果也属于输出，不能在后处理阶段删除或移走。
                Debug.Log($"源文件同时作为输出保留，跳过源文件后处理：'{this.InputFilePath}'。");
                postConversionAction = InputPostConversionAction.None;
            }

            switch (postConversionAction)
            {
                case InputPostConversionAction.None:
                    break;

                case InputPostConversionAction.MoveInArchiveFolder:
                    string basePath = System.IO.Path.GetDirectoryName(this.initialInputPath);
                    string inputFilename = System.IO.Path.GetFileName(this.initialInputPath);
                    string archivePath = basePath + "\\" + this.ConversionPreset.ConversionArchiveFolderName;
                    if (!System.IO.Directory.Exists(archivePath))
                    {
                        System.IO.Directory.CreateDirectory(archivePath);
                    }

                    string newPath = PathHelpers.GenerateUniquePath(archivePath + "\\" + inputFilename);
                    System.IO.File.Move(this.InputFilePath, newPath);
                    Debug.Log($"源文件已移动到归档目录：'{newPath}'");
                    break;

                case InputPostConversionAction.Delete:
                    System.IO.File.Delete(this.InputFilePath);
                    Debug.Log($"源文件已删除：'{this.initialInputPath}'");
                    break;
            }

            Debug.Log(string.Empty);

            this.Progress = 1f;
            this.State = ConversionState.Done;
            this.UserState = Properties.Resources.ConversionStateDone;
            Debug.Log("转换任务已完成。");
        }

        protected void ConversionFailed(string exitingMessage)
        {
            Debug.Log($"失败信息：{exitingMessage}");

            if (this.State == ConversionState.Failed)
            {
                // 已经失败时保留首次错误信息。
                return;
            }

            this.State = ConversionState.Failed;
            this.UserState = Properties.Resources.ConversionStateFailed;
            this.ErrorMessage = exitingMessage;
        }

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                PropertyChangedEventArgs eventArgs = PropertyChangedEventArgsCache.GetOrAdd(propertyName, name => new PropertyChangedEventArgs(name));
                handler(this, eventArgs);
            }
        }

        private void ChangeOutputFileTimestampToMatchOriginal()
        {
            Debug.Log("正在将输出文件时间设置为源文件时间……");

            var originalFileCreationTime = System.IO.File.GetCreationTimeUtc(this.InputFilePath);
            var originalFileLastAccesTime = System.IO.File.GetLastAccessTimeUtc(this.InputFilePath);
            var originalFileLastWriteTime = System.IO.File.GetLastWriteTimeUtc(this.InputFilePath);
            Debug.Log($"  源文件时间：{originalFileCreationTime}、{originalFileLastAccesTime}、{originalFileLastWriteTime}");

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                try
                {
                    System.IO.File.SetCreationTimeUtc(outputFilePath, originalFileCreationTime);
                    System.IO.File.SetLastAccessTimeUtc(outputFilePath, originalFileLastAccesTime);
                    System.IO.File.SetLastWriteTimeUtc(outputFilePath, originalFileLastWriteTime);
                    Debug.Log($"  已更新输出文件时间：'{outputFilePath}'");
                }
                catch (Exception exception)
                {
                    Debug.Log($"无法更新文件时间：'{outputFilePath}'");
                    Debug.Log($"发生异常：{exception}。");
                }
            }

            Debug.Log("文件时间已同步。");
        }

        private bool AllOutputFilesExists()
        {
            if (this.OutputFilePaths == null || this.OutputFilePaths.Length == 0)
            {
                return false;
            }

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                if (!System.IO.File.Exists(outputFilePath))
                {
                    return false;
                }

                OutputFileSnapshot previous;
                if (this.preexistingOutputFiles != null && this.preexistingOutputFiles.TryGetValue(outputFilePath, out previous))
                {
                    OutputFileSnapshot current = CaptureOutputFile(outputFilePath);
                    if (current.Length == previous.Length &&
                        current.LastWriteTimeTicks == previous.LastWriteTimeTicks &&
                        current.ChangeTime == previous.ChangeTime)
                    {
                        // 既有输出完全未更新，不能把它当作本次转换成功的结果。
                        return false;
                    }
                }
            }

            return true;
        }

        private bool InputIsOutputFile()
        {
            string inputPath = System.IO.Path.GetFullPath(this.InputFilePath);
            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                if (string.Equals(inputPath, System.IO.Path.GetFullPath(this.OutputFilePaths[index]), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AtLeastOneOutputFilesExists()
        {
            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                if (System.IO.File.Exists(outputFilePath))
                {
                    return true;
                }
            }

            return false;
        }

        private static OutputFileSnapshot CaptureOutputFile(string filePath)
        {
            var file = new System.IO.FileInfo(filePath);
            long changeTime = 0;
            // ChangeTime 不受恢复原始文件时间的影响，可识别内容相同的合法重写。
            using (SafeFileHandle handle = CreateFile(filePath, 0x80, 0x7, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                FileBasicInformation information;
                if (!handle.IsInvalid && GetFileInformationByHandleEx(handle, 0, out information, Marshal.SizeOf(typeof(FileBasicInformation))))
                {
                    changeTime = information.ChangeTime;
                }
            }

            return new OutputFileSnapshot(file.Length, file.LastWriteTimeUtc.Ticks, changeTime);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileBasicInformation information, int bufferSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInformation
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint Attributes;
        }

        private struct OutputFileSnapshot
        {
            public readonly long Length;
            public readonly long LastWriteTimeTicks;
            public readonly long ChangeTime;

            public OutputFileSnapshot(long length, long lastWriteTimeTicks, long changeTime)
            {
                this.Length = length;
                this.LastWriteTimeTicks = lastWriteTimeTicks;
                this.ChangeTime = changeTime;
            }
        }
    }
}
