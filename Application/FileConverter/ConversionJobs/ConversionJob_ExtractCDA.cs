// <copyright file="ConversionJob_ExtractCDA.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;

    using Diagnostics;
    using Ripper;
    using WaveLib;
    using Yeti.MMedia;

    public class ConversionJob_ExtractCDA : ConversionJob
    {
        private Ripper.CDDrive diskDrive;
        private int cdaTrackNumber = -1;
        private WaveWriter waveWriter;
        private string intermediateFilePath;
        private ConversionJob compressionConversionJob;

        public ConversionJob_ExtractCDA() : base()
        {
        }

        public ConversionJob_ExtractCDA(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override InputPostConversionAction InputPostConversionAction
        {
            get => InputPostConversionAction.None;
        }

        public override void Cancel()
        {
            base.Cancel();

            this.compressionConversionJob?.Cancel();
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            // 获取并检查光驱盘符。
            string pathDriveLetter = PathHelpers.GetPathDriveLetter(this.InputFilePath);
            if (pathDriveLetter.Length == 0)
            {
                this.ConversionFailed(Properties.Resources.ErrorFailToRetrieveInputPathDriveLetter);
                return;
            }

            char driveLetter = pathDriveLetter[0];

            this.diskDrive = new Ripper.CDDrive();
            this.diskDrive.CDRemoved += new EventHandler(this.CdDriveCdRemoved);

            bool driveLetterFound = false;
            char[] driveLetters = Ripper.CDDrive.GetCDDriveLetters();
            for (int index = 0; index < driveLetters.Length; index++)
            {
                driveLetterFound |= driveLetters[index] == driveLetter;
            }

            if (!driveLetterFound)
            {
                Debug.Log($"无效的光驱盘符：{driveLetter}。");
                this.ConversionFailed(Properties.Resources.ErrorFailToRetrieveInputPathDriveLetter);
                return;
            }

            // 获取音轨编号。
            try
            {
                this.cdaTrackNumber = PathHelpers.GetCDATrackNumber(this.InputFilePath);
            }
            catch (Exception)
            {
                Debug.Log($"输入路径：'{this.InputFilePath}'。");
                this.ConversionFailed(Properties.Resources.ErrorFailToRetrieveTrackNumber);
                return;
            }

            if (this.diskDrive.IsOpened)
            {
                this.ConversionFailed(Properties.Resources.ErrorFailToUseCDDriveOpen);
                return;
            }

            if (!this.diskDrive.Open(driveLetter))
            {
                this.ConversionFailed(string.Format(Properties.Resources.ErrorFailToReadCDDrive, driveLetter));
                return;
            }

            // 生成临时中间文件路径。
            string fileName = Path.GetFileName(this.OutputFilePath);
            string tempPath = Path.GetTempPath();
            this.intermediateFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + ".wav");

            // 创建压缩音轨的子任务。
            ConversionPreset compressionPreset = new ConversionPreset("Cda encoding", this.ConversionPreset, "wav");
            this.compressionConversionJob = ConversionJobFactory.Create(compressionPreset, this.intermediateFilePath);
            this.compressionConversionJob.PrepareConversion(this.OutputFilePath);
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            Debug.Log("开始提取 CD 音轨。");

            this.UserState = Properties.Resources.ConversionStateExtraction;

            if (!this.diskDrive.IsCDReady())
            {
                this.ConversionFailed(Properties.Resources.ErrorCDDriveNotReady);
                return;
            }

            if (!this.diskDrive.Refresh())
            {
                Debug.Log("无法刷新光驱信息。");
                this.ConversionFailed(Properties.Resources.ErrorCDDriveNotReady);
                return;
            }

            if (!this.diskDrive.LockCD())
            {
                Debug.Log("无法锁定光驱。");
                this.ConversionFailed(Properties.Resources.ErrorCDDriveNotReady);
                return;
            }

            try
            {
                WaveFormat waveFormat = new WaveFormat(44100, 16, 2);

                using (Stream waveStream = new FileStream(this.intermediateFilePath, FileMode.Create, FileAccess.Write))
                using (this.waveWriter = new WaveWriter(waveStream, waveFormat, this.diskDrive.TrackSize(this.cdaTrackNumber)))
                {
                    this.diskDrive.ReadTrack(this.cdaTrackNumber, this.WriteWaveData, this.CdReadProgress);
                }
            }
            finally
            {
                this.waveWriter = null;
                this.ReleaseDiskDrive();
                this.StateFlags = ConversionFlags.None;
            }

            if (this.CancelIsRequested || this.State == ConversionState.Failed)
            {
                return;
            }

            if (!File.Exists(this.intermediateFilePath))
            {
                this.ConversionFailed(Properties.Resources.ErrorCDAExtractionFailed);
                return;
            }

            Debug.Log($"音轨已提取到：{this.intermediateFilePath}。");
            Debug.Log(string.Empty);
            Debug.Log("开始压缩音轨。");

            this.UserState = Properties.Resources.ConversionStateConversion;

            this.StartChildConversion(this.compressionConversionJob, false);
        }

        protected override void ReleaseResources()
        {
            try
            {
                this.ReleaseDiskDrive();
            }
            finally
            {
                DeleteIntermediateFile(this.intermediateFilePath);
            }
        }

        private void ReleaseDiskDrive()
        {
            Ripper.CDDrive drive = this.diskDrive;
            this.diskDrive = null;
            if (drive == null)
            {
                return;
            }

            drive.CDRemoved -= this.CdDriveCdRemoved;
            try
            {
                if (drive.IsOpened)
                {
                    try
                    {
                        drive.UnLockCD();
                    }
                    finally
                    {
                        drive.Close();
                    }
                }
            }
            finally
            {
                drive.Dispose();
            }
        }

        private void WriteWaveData(object sender, DataReadEventArgs eventArgs)
        {
            this.waveWriter?.Write(eventArgs.Data, 0, (int)eventArgs.DataSize);
        }

        private void CdReadProgress(object sender, ReadProgressEventArgs eventArgs)
        {
            if (this.CancelIsRequested)
            {
                eventArgs.CancelRead = true;
                return;
            }

            this.Progress = (float)eventArgs.BytesRead / (float)eventArgs.Bytes2Read;

            eventArgs.CancelRead |= this.State != ConversionState.InProgress;
        }

        private void CdDriveCdRemoved(object sender, System.EventArgs eventArgs)
        {
            this.ConversionFailed("光盘已弹出。");
        }

    }
}
