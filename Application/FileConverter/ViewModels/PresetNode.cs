// <copyright file="PresetNode.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ViewModels
{
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;

    using CommunityToolkit.Mvvm.ComponentModel;

    public abstract class AbstractTreeNode : ObservableObject, IDataErrorInfo
    {
        private PresetFolderNode parent;

        protected AbstractTreeNode(PresetFolderNode parent)
        {
            this.Parent = parent;
        }

#if DEBUG
        protected AbstractTreeNode()
        {
        }
#endif

        public PresetFolderNode Parent
        {
            get => this.parent;
            set
            {
                PresetFolderNode previousParent = this.parent;
                if (this.SetProperty(ref this.parent, value))
                {
                    previousParent?.NotifyDisplayNameChanged();
                    this.NotifyDisplayNameChanged();
                }
            }
        }

        public abstract string Name
        {
            get;
            set;
        }

        public abstract string DisplayName { get; }

        internal abstract bool HasOnlyDefaultPresets { get; }

        internal void NotifyDisplayNameChanged()
        {
            this.OnPropertyChanged(nameof(this.DisplayName));
            this.Parent?.NotifyDisplayNameChanged();
        }

        public string this[string columnName] => this.Validate(columnName);

        public bool HasError => !string.IsNullOrEmpty(this.Error);

        public string Error
        {
            get
            {
                if (this.Parent == null)
                {
                    // 根文件夹不参与预设规则校验。
                    return string.Empty;
                }

                string errorString = this.Validate("Name");
                if (!string.IsNullOrEmpty(errorString))
                {
                    return errorString;
                }

                errorString = this.Validate("OutputFileNameTemplate");
                if (!string.IsNullOrEmpty(errorString))
                {
                    return errorString;
                }

                return string.Empty;
            }
        }

        protected virtual string Validate(string propertyName)
        {
            // 校验失败时返回错误消息，否则返回空字符串。
            switch (propertyName)
            {
                case "Name":
                {
                    if (string.IsNullOrEmpty(this.Name))
                    {
                        return "预设名称不能为空。";
                    }

                    if (this.Name.Contains(";"))
                    {
                        return "预设名称不能包含字符“;”。";
                    }

                    if (this.Name.Contains("/"))
                    {
                        return "预设名称不能包含字符“/”。";
                    }

                    if (this.Parent != null)
                    {
                        int count = this.Parent.Children.Count(node => node.Name == this.Name);
                        if (count > 1)
                        {
                            return "预设名称已存在。";
                        }
                    }
                }

                break;
            }

            return string.Empty;
        }
    }

    public class PresetNode : AbstractTreeNode
    {
        private ConversionPreset preset;

        public PresetNode(ConversionPreset preset, PresetFolderNode parent) : base(parent)
        {
            this.SetPreset(preset);
        }

#if DEBUG
        public PresetNode() : base()
        {
        }
#endif

        public ConversionPreset Preset
        {
            get => this.preset;
#if DEBUG
            set => this.SetPreset(value);
#endif
        }

        public override string DisplayName => this.Preset?.DisplayName;

        internal override bool HasOnlyDefaultPresets => this.Preset?.IsDefaultSettings == true;

        public override string Name
        {
            get => this.Preset.ShortName;

            set
            {
                this.Preset.ShortName = value;
            }
        }

        private void SetPreset(ConversionPreset value)
        {
            if (this.preset != null)
            {
                PropertyChangedEventManager.RemoveHandler(this.preset, this.PresetDisplayNameChanged, nameof(ConversionPreset.DisplayName));
            }

            this.preset = value;
            if (this.preset != null)
            {
                // 弱订阅避免旧预设模型保留已关闭的设置窗口节点。
                PropertyChangedEventManager.AddHandler(this.preset, this.PresetDisplayNameChanged, nameof(ConversionPreset.DisplayName));
            }

            this.OnPropertyChanged(nameof(this.Preset));
            this.PresetDisplayNameChanged(this.preset, null);
        }

        private void PresetDisplayNameChanged(object sender, PropertyChangedEventArgs args)
        {
            this.OnPropertyChanged(nameof(this.Name));
            this.OnPropertyChanged(nameof(this.HasError));
            this.NotifyDisplayNameChanged();
        }

        public string OutputFileNameTemplate
        {
            get => this.Preset.OutputFileNameTemplate;

            set
            {
                this.Preset.OutputFileNameTemplate = value;
                this.OnPropertyChanged();
            }
        }

        protected override string Validate(string propertyName)
        {
            string error = base.Validate(propertyName);
            if (!string.IsNullOrEmpty(error))
            {
                return error;
            }

            // 校验失败时返回错误消息，否则返回空字符串。
            switch (propertyName)
            {
                case "OutputFileNameTemplate":
                    {
                        string sampleOutputFilePath = this.Preset.GenerateOutputFilePath(FileConverter.Properties.Resources.OutputFileNameTemplateSample, 1, 3);
                        if (string.IsNullOrEmpty(sampleOutputFilePath))
                        {
                            return "输出文件名模板生成的结果不能为空。";
                        }

                        if (!PathHelpers.IsPathValid(sampleOutputFilePath))
                        {
                            // 逐项检查路径并提供明确的错误提示。
                            // 检查驱动器号。
                            if (!PathHelpers.IsPathDriveLetterValid(sampleOutputFilePath))
                            {
                                return "输出文件名模板必须指定根路径（例如 C:\\；可用 (p) 表示输入文件路径）。";
                            }

                            // 检查文件名。
                            string filename = PathHelpers.GetFileName(sampleOutputFilePath);
                            if (filename == null)
                            {
                                return "输出文件名不能为空（可用 (f) 表示输入文件名）。";
                            }

                            char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
                            for (int index = 0; index < invalidFileNameChars.Length; index++)
                            {
                                if (filename.Contains(invalidFileNameChars[index]))
                                {
                                    return "输出文件名不能包含字符“" + invalidFileNameChars[index] + "”。";
                                }
                            }

                            // 检查目录名称。
                            string path = sampleOutputFilePath.Substring(3, sampleOutputFilePath.Length - 3 - filename.Length);
                            char[] invalidPathChars = System.IO.Path.GetInvalidPathChars();
                            for (int index = 0; index < invalidPathChars.Length; index++)
                            {
                                if (string.IsNullOrEmpty(path))
                                {
                                    return "输出目录名称不能为空（可用 (d0)、(d1) 等表示输入文件的各级父目录名称）。";
                                }

                                if (path.Contains(invalidPathChars[index]))
                                {
                                    return "输出目录名称不能包含字符“" + invalidPathChars[index] + "”。";
                                }
                            }

                            string[] directories = path.Split('\\');
                            for (int index = 0; index < directories.Length; ++index)
                            {
                                string directoryName = directories[index];
                                if (string.IsNullOrEmpty(directoryName))
                                {
                                    return "输出目录名称不能为空（可用 (d0)、(d1) 等表示输入文件的各级父目录名称）。";
                                }
                            }

                            return "输出文件名模板无效。";
                        }
                    }

                    break;
            }

            return string.Empty;
        }
    }

    public class PresetFolderNode : AbstractTreeNode
    {
        private string name;
        private ObservableCollection<AbstractTreeNode> children = new ObservableCollection<AbstractTreeNode>();

        public PresetFolderNode(string name, PresetFolderNode parent) : base(parent)
        {
            this.children.CollectionChanged += this.ChildrenChanged;
            this.Name = name;
        }

#if DEBUG
        public PresetFolderNode() : this(null, null)
        {
        }
#endif

        public override string Name
        {
            get => this.name;

            set
            {
                if (this.SetProperty(ref this.name, value))
                {
                    this.OnPropertyChanged(nameof(this.HasError));
                    this.NotifyDisplayNameChanged();
                }
            }
        }

        public override string DisplayName => FileConverterExtension.PresetDisplayNames.GetName(this.Name, this.HasOnlyDefaultPresets);

        internal override bool HasOnlyDefaultPresets => this.Children.Count > 0 && this.Children.All(child => child.HasOnlyDefaultPresets);

        public ObservableCollection<AbstractTreeNode> Children
        {
            get => this.children;

            set
            {
                if (ReferenceEquals(this.children, value))
                {
                    return;
                }

                this.children.CollectionChanged -= this.ChildrenChanged;
                this.children = value ?? new ObservableCollection<AbstractTreeNode>();
                this.children.CollectionChanged += this.ChildrenChanged;
                foreach (AbstractTreeNode child in this.children)
                {
                    child.Parent = this;
                }

                this.OnPropertyChanged();
                this.NotifyDisplayNameChanged();
            }
        }

        private void ChildrenChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.NewItems != null)
            {
                foreach (AbstractTreeNode child in args.NewItems)
                {
                    child.Parent = this;
                }
            }

            this.NotifyDisplayNameChanged();
        }

        public bool IsNodeInHierarchy(AbstractTreeNode node, bool recurse)
        {
            Diagnostics.Debug.Assert(node != null, "node != null");
            foreach (ObservableObject child in this.Children)
            {
                if (child == node)
                {
                    return true;
                }

                if (recurse && child is PresetFolderNode subFolder)
                {
                    if (subFolder.IsNodeInHierarchy(node, true))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
