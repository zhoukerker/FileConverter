// 简体中文资源访问器；新增资源时同步添加同名属性。

namespace FileConverter.Properties
{
    using System.ComponentModel;
    using System.Globalization;
    using System.Resources;

    public class Resources
    {
        private static readonly ResourceManager resourceManager = new ResourceManager("FileConverter.Properties.Resources", typeof(Resources).Assembly);

        internal Resources()
        {
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static ResourceManager ResourceManager => resourceManager;

        // 保留文化覆盖接口，旧调用方仍可使用；中性资源始终为简体中文。
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static CultureInfo Culture { get; set; }

        public static string About => ResourceManager.GetString(nameof(About), Culture);
        public static string ActionWhenConversionSucceedDescription => ResourceManager.GetString(nameof(ActionWhenConversionSucceedDescription), Culture);
        public static string ActionWhenConversionSucceedTitle => ResourceManager.GetString(nameof(ActionWhenConversionSucceedTitle), Culture);
        public static string AddNewPreset => ResourceManager.GetString(nameof(AddNewPreset), Culture);
        public static string AdvancedMode => ResourceManager.GetString(nameof(AdvancedMode), Culture);
        public static string Application => ResourceManager.GetString(nameof(Application), Culture);
        public static string ApplicationIsTerminating => ResourceManager.GetString(nameof(ApplicationIsTerminating), Culture);
        public static string ApplicationWillTerminateInMultipleSeconds => ResourceManager.GetString(nameof(ApplicationWillTerminateInMultipleSeconds), Culture);
        public static string ApplicationWillTerminateInOneSecond => ResourceManager.GetString(nameof(ApplicationWillTerminateInOneSecond), Culture);
        public static string AudioTitle => ResourceManager.GetString(nameof(AudioTitle), Culture);
        public static string AutomaticallyCheckForUpdates => ResourceManager.GetString(nameof(AutomaticallyCheckForUpdates), Culture);
        public static string AutomaticallyExitWhenAllConversionsFinished => ResourceManager.GetString(nameof(AutomaticallyExitWhenAllConversionsFinished), Culture);
        public static string CancelJobTooltip => ResourceManager.GetString(nameof(CancelJobTooltip), Culture);
        public static string ChannelCountTitle => ResourceManager.GetString(nameof(ChannelCountTitle), Culture);
        public static string ChannelCountTooltip => ResourceManager.GetString(nameof(ChannelCountTooltip), Culture);
        public static string ClampToLowestPowerOfTwoSize => ResourceManager.GetString(nameof(ClampToLowestPowerOfTwoSize), Culture);
        public static string Close => ResourceManager.GetString(nameof(Close), Culture);
        public static string ConversionArchives => ResourceManager.GetString(nameof(ConversionArchives), Culture);
        public static string ConversionPresets => ResourceManager.GetString(nameof(ConversionPresets), Culture);
        public static string ConversionQueueTitle => ResourceManager.GetString(nameof(ConversionQueueTitle), Culture);
        public static string ConversionStateConversion => ResourceManager.GetString(nameof(ConversionStateConversion), Culture);
        public static string ConversionStateDone => ResourceManager.GetString(nameof(ConversionStateDone), Culture);
        public static string ConversionStateExtraction => ResourceManager.GetString(nameof(ConversionStateExtraction), Culture);
        public static string ConversionStateFailed => ResourceManager.GetString(nameof(ConversionStateFailed), Culture);
        public static string ConversionStateInQueue => ResourceManager.GetString(nameof(ConversionStateInQueue), Culture);
        public static string ConversionStatePrepareConversion => ResourceManager.GetString(nameof(ConversionStatePrepareConversion), Culture);
        public static string ConversionStateReadDocument => ResourceManager.GetString(nameof(ConversionStateReadDocument), Culture);
        public static string ConversionStateReadIntputImage => ResourceManager.GetString(nameof(ConversionStateReadIntputImage), Culture);
        public static string ConvertedFrom => ResourceManager.GetString(nameof(ConvertedFrom), Culture);
        public static string CopyFilesInClipboardAfterConversion => ResourceManager.GetString(nameof(CopyFilesInClipboardAfterConversion), Culture);
        public static string CreateANewFolder => ResourceManager.GetString(nameof(CreateANewFolder), Culture);
        public static string DefaultFolderName => ResourceManager.GetString(nameof(DefaultFolderName), Culture);
        public static string DefaultPresetName => ResourceManager.GetString(nameof(DefaultPresetName), Culture);
        public static string Diagnostics => ResourceManager.GetString(nameof(Diagnostics), Culture);
        public static string DiagnosticsButtonTooltip => ResourceManager.GetString(nameof(DiagnosticsButtonTooltip), Culture);
        public static string DocumentationButtonDescription => ResourceManager.GetString(nameof(DocumentationButtonDescription), Culture);
        public static string DonateButtonTitle => ResourceManager.GetString(nameof(DonateButtonTitle), Culture);
        public static string DonateDescription => ResourceManager.GetString(nameof(DonateDescription), Culture);
        public static string DownloadAndInstallButtonDescription => ResourceManager.GetString(nameof(DownloadAndInstallButtonDescription), Culture);
        public static string DownloadAndInstallButtonTitle => ResourceManager.GetString(nameof(DownloadAndInstallButtonTitle), Culture);
        public static string DownloadingChangeLog => ResourceManager.GetString(nameof(DownloadingChangeLog), Culture);
        public static string DuplicatePreset => ResourceManager.GetString(nameof(DuplicatePreset), Culture);
        public static string Encoding => ResourceManager.GetString(nameof(Encoding), Culture);
        public static string EncodingSpeed => ResourceManager.GetString(nameof(EncodingSpeed), Culture);
        public static string Error => ResourceManager.GetString(nameof(Error), Culture);
        public static string ErrorCanceled => ResourceManager.GetString(nameof(ErrorCanceled), Culture);
        public static string ErrorCantFindFFMPEG => ResourceManager.GetString(nameof(ErrorCantFindFFMPEG), Culture);
        public static string ErrorCantFindOutputFiles => ResourceManager.GetString(nameof(ErrorCantFindOutputFiles), Culture);
        public static string ErrorCantLoadSettings => ResourceManager.GetString(nameof(ErrorCantLoadSettings), Culture);
        public static string ErrorCDAExtractionFailed => ResourceManager.GetString(nameof(ErrorCDAExtractionFailed), Culture);
        public static string ErrorCDDriveNotReady => ResourceManager.GetString(nameof(ErrorCDDriveNotReady), Culture);
        public static string ErrorConversionFailedWithOutput => ResourceManager.GetString(nameof(ErrorConversionFailedWithOutput), Culture);
        public static string ErrorDuringJobInitialization => ResourceManager.GetString(nameof(ErrorDuringJobInitialization), Culture);
        public static string ErrorFailedToLaunchFFMPEG => ResourceManager.GetString(nameof(ErrorFailedToLaunchFFMPEG), Culture);
        public static string ErrorFailToCreateOutputPathFolders => ResourceManager.GetString(nameof(ErrorFailToCreateOutputPathFolders), Culture);
        public static string ErrorFailToGenerateUniqueOutputPath => ResourceManager.GetString(nameof(ErrorFailToGenerateUniqueOutputPath), Culture);
        public static string ErrorFailToReadCDDrive => ResourceManager.GetString(nameof(ErrorFailToReadCDDrive), Culture);
        public static string ErrorFailToRetrieveInputPathDriveLetter => ResourceManager.GetString(nameof(ErrorFailToRetrieveInputPathDriveLetter), Culture);
        public static string ErrorFailToRetrieveTrackNumber => ResourceManager.GetString(nameof(ErrorFailToRetrieveTrackNumber), Culture);
        public static string ErrorFailToUseCDDriveOpen => ResourceManager.GetString(nameof(ErrorFailToUseCDDriveOpen), Culture);
        public static string ErrorInputTypeIncompatibleWithOutputType => ResourceManager.GetString(nameof(ErrorInputTypeIncompatibleWithOutputType), Culture);
        public static string ErrorInvalidOutputPath => ResourceManager.GetString(nameof(ErrorInvalidOutputPath), Culture);
        public static string ErrorMicrosoftExcelIsNotAvailable => ResourceManager.GetString(nameof(ErrorMicrosoftExcelIsNotAvailable), Culture);
        public static string ErrorMicrosoftOfficeIsNotAvailable => ResourceManager.GetString(nameof(ErrorMicrosoftOfficeIsNotAvailable), Culture);
        public static string ErrorMicrosoftPowerPointIsNotAvailable => ResourceManager.GetString(nameof(ErrorMicrosoftPowerPointIsNotAvailable), Culture);
        public static string ErrorMicrosoftWordIsNotAvailable => ResourceManager.GetString(nameof(ErrorMicrosoftWordIsNotAvailable), Culture);
        public static string ErrorUnableToUseMicrosoftOffice => ResourceManager.GetString(nameof(ErrorUnableToUseMicrosoftOffice), Culture);
        public static string ErrorUnsupportedOutputFormat => ResourceManager.GetString(nameof(ErrorUnsupportedOutputFormat), Culture);
        public static string ExitWaitingDuration => ResourceManager.GetString(nameof(ExitWaitingDuration), Culture);
        public static string ExportSelectedPresets => ResourceManager.GetString(nameof(ExportSelectedPresets), Culture);
        public static string FFMPEGCustomArguments => ResourceManager.GetString(nameof(FFMPEGCustomArguments), Culture);
        public static string FFMPEGCustomArgumentsTooltip => ResourceManager.GetString(nameof(FFMPEGCustomArgumentsTooltip), Culture);
        public static string FileConverterStartHelp1 => ResourceManager.GetString(nameof(FileConverterStartHelp1), Culture);
        public static string FileConverterStartHelp2 => ResourceManager.GetString(nameof(FileConverterStartHelp2), Culture);
        public static string FileConverterStartHelp3 => ResourceManager.GetString(nameof(FileConverterStartHelp3), Culture);
        public static string FileNameTemplate => ResourceManager.GetString(nameof(FileNameTemplate), Culture);
        public static string FramesPerSecond => ResourceManager.GetString(nameof(FramesPerSecond), Culture);
        public static string GitHubButtonDescription => ResourceManager.GetString(nameof(GitHubButtonDescription), Culture);
        public static string HardwareAccelerationMode => ResourceManager.GetString(nameof(HardwareAccelerationMode), Culture);
        public static string HardwareAccelerationModeAMFName => ResourceManager.GetString(nameof(HardwareAccelerationModeAMFName), Culture);
        public static string HardwareAccelerationModeCUDAName => ResourceManager.GetString(nameof(HardwareAccelerationModeCUDAName), Culture);
        public static string HardwareAccelerationModeD3D11Name => ResourceManager.GetString(nameof(HardwareAccelerationModeD3D11Name), Culture);
        public static string HardwareAccelerationModeDXVA2Name => ResourceManager.GetString(nameof(HardwareAccelerationModeDXVA2Name), Culture);
        public static string HardwareAccelerationModeOffName => ResourceManager.GetString(nameof(HardwareAccelerationModeOffName), Culture);
        public static string HardwareAccelerationModeOpenCLName => ResourceManager.GetString(nameof(HardwareAccelerationModeOpenCLName), Culture);
        public static string HardwareAccelerationModeVulkanName => ResourceManager.GetString(nameof(HardwareAccelerationModeVulkanName), Culture);
        public static string Help => ResourceManager.GetString(nameof(Help), Culture);
        public static string ImportPresets => ResourceManager.GetString(nameof(ImportPresets), Culture);
        public static string InputExample => ResourceManager.GetString(nameof(InputExample), Culture);
        public static string InputFormats => ResourceManager.GetString(nameof(InputFormats), Culture);
        public static string InputPostConversionActionDeleteName => ResourceManager.GetString(nameof(InputPostConversionActionDeleteName), Culture);
        public static string InputPostConversionActionMoveInArchiveFolderName => ResourceManager.GetString(nameof(InputPostConversionActionMoveInArchiveFolderName), Culture);
        public static string InputPostConversionActionNoneName => ResourceManager.GetString(nameof(InputPostConversionActionNoneName), Culture);
        public static string InstallButtonDescription => ResourceManager.GetString(nameof(InstallButtonDescription), Culture);
        public static string InstallButtonTitle => ResourceManager.GetString(nameof(InstallButtonTitle), Culture);
        public static string IssueButtonDescription => ResourceManager.GetString(nameof(IssueButtonDescription), Culture);
        public static string Language => ResourceManager.GetString(nameof(Language), Culture);
        public static string LicenceHeader1 => ResourceManager.GetString(nameof(LicenceHeader1), Culture);
        public static string LicenceHeader2 => ResourceManager.GetString(nameof(LicenceHeader2), Culture);
        public static string LicenceHeader3 => ResourceManager.GetString(nameof(LicenceHeader3), Culture);
        public static string MaximumNumberOfSimultaneousConversions => ResourceManager.GetString(nameof(MaximumNumberOfSimultaneousConversions), Culture);
        public static string MonoOption => ResourceManager.GetString(nameof(MonoOption), Culture);
        public static string MoveDownSelectedPreset => ResourceManager.GetString(nameof(MoveDownSelectedPreset), Culture);
        public static string MoveUpSelectedPreset => ResourceManager.GetString(nameof(MoveUpSelectedPreset), Culture);
        public static string Mp3CbrDescription => ResourceManager.GetString(nameof(Mp3CbrDescription), Culture);
        public static string Mp3VbrDescription => ResourceManager.GetString(nameof(Mp3VbrDescription), Culture);
        public static string NinetyDegreesRotationTitle => ResourceManager.GetString(nameof(NinetyDegreesRotationTitle), Culture);
        public static string NinetyDegreesRotationTooltip => ResourceManager.GetString(nameof(NinetyDegreesRotationTooltip), Culture);
        public static string NoPresetSelected => ResourceManager.GetString(nameof(NoPresetSelected), Culture);
        public static string Ok => ResourceManager.GetString(nameof(Ok), Culture);
        public static string OneEightyDegreesRotationTitle => ResourceManager.GetString(nameof(OneEightyDegreesRotationTitle), Culture);
        public static string OneEightyDegreesRotationTooltip => ResourceManager.GetString(nameof(OneEightyDegreesRotationTooltip), Culture);
        public static string OutputExample => ResourceManager.GetString(nameof(OutputExample), Culture);
        public static string OutputFileNameTemplateSample => ResourceManager.GetString(nameof(OutputFileNameTemplateSample), Culture);
        public static string OutputFilePathTemplateHelp => ResourceManager.GetString(nameof(OutputFilePathTemplateHelp), Culture);
        public static string OutputFormat => ResourceManager.GetString(nameof(OutputFormat), Culture);
        public static string Preset => ResourceManager.GetString(nameof(Preset), Culture);
        public static string PresetFolder => ResourceManager.GetString(nameof(PresetFolder), Culture);
        public static string PresetFolderName => ResourceManager.GetString(nameof(PresetFolderName), Culture);
        public static string PresetName => ResourceManager.GetString(nameof(PresetName), Culture);
        public static string Quality => ResourceManager.GetString(nameof(Quality), Culture);
        public static string RecommendedBitrateRangeInBlue => ResourceManager.GetString(nameof(RecommendedBitrateRangeInBlue), Culture);
        public static string RemoveSelectedPreset => ResourceManager.GetString(nameof(RemoveSelectedPreset), Culture);
        public static string Rotate => ResourceManager.GetString(nameof(Rotate), Culture);
        public static string SameChannelCountOption => ResourceManager.GetString(nameof(SameChannelCountOption), Culture);
        public static string Save => ResourceManager.GetString(nameof(Save), Culture);
        public static string Scale => ResourceManager.GetString(nameof(Scale), Culture);
        public static string SeeChangeLog => ResourceManager.GetString(nameof(SeeChangeLog), Culture);
        public static string Settings => ResourceManager.GetString(nameof(Settings), Culture);
        public static string SettingsButtonTooltip => ResourceManager.GetString(nameof(SettingsButtonTooltip), Culture);
        public static string StereoOption => ResourceManager.GetString(nameof(StereoOption), Culture);
        public static string StringAnimatedImageName => ResourceManager.GetString(nameof(StringAnimatedImageName), Culture);
        public static string StringAudioName => ResourceManager.GetString(nameof(StringAudioName), Culture);
        public static string StringDocumentName => ResourceManager.GetString(nameof(StringDocumentName), Culture);
        public static string StringImageName => ResourceManager.GetString(nameof(StringImageName), Culture);
        public static string StringMiscName => ResourceManager.GetString(nameof(StringMiscName), Culture);
        public static string StringVideoName => ResourceManager.GetString(nameof(StringVideoName), Culture);
        public static string TwoSeventyDegreesRotationTitle => ResourceManager.GetString(nameof(TwoSeventyDegreesRotationTitle), Culture);
        public static string TwoSeventyDegreesRotationTooltip => ResourceManager.GetString(nameof(TwoSeventyDegreesRotationTooltip), Culture);
        public static string UpgradeAvailable => ResourceManager.GetString(nameof(UpgradeAvailable), Culture);
        public static string UpgradeDownloadInProgress => ResourceManager.GetString(nameof(UpgradeDownloadInProgress), Culture);
        public static string UpgradeWindowTitle => ResourceManager.GetString(nameof(UpgradeWindowTitle), Culture);
        public static string VideoEncodingQualityTooltip => ResourceManager.GetString(nameof(VideoEncodingQualityTooltip), Culture);
        public static string VideoEncodingSpeedFasterName => ResourceManager.GetString(nameof(VideoEncodingSpeedFasterName), Culture);
        public static string VideoEncodingSpeedFastName => ResourceManager.GetString(nameof(VideoEncodingSpeedFastName), Culture);
        public static string VideoEncodingSpeedMediumName => ResourceManager.GetString(nameof(VideoEncodingSpeedMediumName), Culture);
        public static string VideoEncodingSpeedSlowerName => ResourceManager.GetString(nameof(VideoEncodingSpeedSlowerName), Culture);
        public static string VideoEncodingSpeedSlowName => ResourceManager.GetString(nameof(VideoEncodingSpeedSlowName), Culture);
        public static string VideoEncodingSpeedSuperFastName => ResourceManager.GetString(nameof(VideoEncodingSpeedSuperFastName), Culture);
        public static string VideoEncodingSpeedTooltip => ResourceManager.GetString(nameof(VideoEncodingSpeedTooltip), Culture);
        public static string VideoEncodingSpeedUltraFastName => ResourceManager.GetString(nameof(VideoEncodingSpeedUltraFastName), Culture);
        public static string VideoEncodingSpeedVeryFastName => ResourceManager.GetString(nameof(VideoEncodingSpeedVeryFastName), Culture);
        public static string VideoEncodingSpeedVerySlowName => ResourceManager.GetString(nameof(VideoEncodingSpeedVerySlowName), Culture);
        public static string VideoTitle => ResourceManager.GetString(nameof(VideoTitle), Culture);
        public static string Wav16bitsDescription => ResourceManager.GetString(nameof(Wav16bitsDescription), Culture);
        public static string Wav24bitsDescription => ResourceManager.GetString(nameof(Wav24bitsDescription), Culture);
        public static string Wav32bitsDescription => ResourceManager.GetString(nameof(Wav32bitsDescription), Culture);
        public static string Wav8bitsDescription => ResourceManager.GetString(nameof(Wav8bitsDescription), Culture);
        public static string WebsiteButtonDescription => ResourceManager.GetString(nameof(WebsiteButtonDescription), Culture);
        public static string ZeroDegreesRotationTitle => ResourceManager.GetString(nameof(ZeroDegreesRotationTitle), Culture);
        public static string ZeroDegreesRotationTooltip => ResourceManager.GetString(nameof(ZeroDegreesRotationTooltip), Culture);
    }
}
