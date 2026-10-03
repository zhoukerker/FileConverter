// <copyright file="FileConverterExtension.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Forms;

    using SharpShell.Attributes;
    using SharpShell.SharpContextMenu;

    /// <summary>
    /// 文件转换器的资源管理器右键菜单扩展。
    /// </summary>
    [ComVisible(true), Guid("AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90")]
    [COMServerAssociation(AssociationType.AllFiles)]
    public class FileConverterExtension : SharpContextMenu
    {
        private const int MaximumProcessArgumentsLength = 8000; // https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/command-line-string-limitation

        private PresetReference[] presetReferences;
        private readonly List<MenuEntry> menuEntries = new List<MenuEntry>();
        private readonly Dictionary<string, MenuEntry> menuEntriesByName = new Dictionary<string, MenuEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PresetReference>> presetsByExtension = new Dictionary<string, List<PresetReference>>(StringComparer.Ordinal);
        private readonly HashSet<string> extensionCache = new HashSet<string>(StringComparer.Ordinal);

        private class MenuEntry
        {
            public PresetReference PresetReference;
            public bool Enabled;
            public int ExtensionRefCount;

            public MenuEntry(PresetReference presetReference)
            {
                this.PresetReference = presetReference;
            }
        }

        private bool DisplayPresetIcons
        {
            get
            {
                string displayPresetIcons = PathHelpers.FileConverterRegistryKey.GetValue("DisplayPresetIcons") as string;
                return bool.TryParse(displayPresetIcons, out bool value) && value;
            }
        }

        protected override bool CanShowMenu()
        {
            this.RefreshExtensionCacheFromSelectedItems();

            this.LoadExtensionSettingsIfNecessary();
            foreach (string extension in this.extensionCache)
            {
                if (this.presetsByExtension.ContainsKey(extension))
                {
                    return true;
                }
            }

            return false;
        }

        protected override ContextMenuStrip CreateMenu()
        {
            this.RefreshPresetList();

            bool displayPresetIcons = this.DisplayPresetIcons;

            ContextMenuStrip menu = new ContextMenuStrip();
            List<Bitmap> menuImages = new List<Bitmap>(4);
            menu.Disposed += (sender, args) =>
            {
                foreach (Bitmap image in menuImages)
                {
                    image.Dispose();
                }
            };

            Bitmap folderImage = null;
            Bitmap presetImage = null;

            ToolStripMenuItem fileConverterItem = new ToolStripMenuItem
            {
                Text = GetText("ApplicationMenuTitle"),
                Image = CreateMenuImage(Properties.Resources.ApplicationIcon, menuImages),
            };

            int menuItemIndex = 0;
            foreach (MenuEntry menuEntry in this.menuEntries)
            {
                menuItemIndex++;

                ToolStripMenuItem root = fileConverterItem;
                if (menuEntry.PresetReference.Folders != null)
                {
                    foreach (string folder in menuEntry.PresetReference.Folders)
                    {
                        ToolStripItem[] folderItems = root.DropDownItems.Find(folder, false);
                        if (folderItems.Length == 0)
                        {
                            if (folderImage == null)
                            {
                                folderImage = CreateMenuImage(Properties.Resources.FolderIcon, menuImages);
                            }

                            ToolStripMenuItem folderItem = new ToolStripMenuItem
                            {
                                Name = folder,
                                Text = PresetDisplayNames.GetName(folder, menuEntry.PresetReference.IsDefaultSettings),
                                Image = folderImage,
                            };

                            root.DropDownItems.Add(folderItem);
                            root = folderItem;
                        }
                        else
                        {
                            root = folderItems[0] as ToolStripMenuItem;
                            if (root != null && !menuEntry.PresetReference.IsDefaultSettings)
                            {
                                // 混合自定义预设的目录保留用户原始名称。
                                root.Text = folder;
                            }
                        }

                        if (root == null)
                        {
                            break;
                        }
                    }
                }

                if (root == null)
                {
                    // 文件夹创建失败时将预设放回菜单根节点。
                    root = fileConverterItem;
                }

                // 保留不可见的零宽空格，避免 Windows Forms 的同名菜单项冲突。
                string uniqueSuffix = new string('\u200B', menuItemIndex);
                string displayText = menuEntry.PresetReference.DisplayName + uniqueSuffix;

                ToolStripMenuItem subItem = new ToolStripMenuItem
                {
                    Text = displayText,
                    Enabled = menuEntry.Enabled
                };

                if (displayPresetIcons)
                {
                    if (presetImage == null)
                    {
                        presetImage = CreateMenuImage(Properties.Resources.PresetIcon, menuImages);
                    }

                    subItem.Image = presetImage;
                }

                root.DropDownItems.Add(subItem);
                subItem.Click += (sender, args) => this.ConvertFiles(menuEntry.PresetReference.FullName);
            }

            if (this.menuEntries.Count > 0)
            {
                fileConverterItem.DropDownItems.Add(new ToolStripSeparator());
            }

            {
                ToolStripMenuItem subItem = new ToolStripMenuItem
                {
                    Text = GetText("ConfigurePresetsMenuTitle"),
                    Image = CreateMenuImage(Properties.Resources.SettingsIcon, menuImages),
                };

                fileConverterItem.DropDownItems.Add(subItem);
                subItem.Click += (sender, args) => this.OpenSettings();
            }

            menu.Items.Add(fileConverterItem);

            return menu;
        }

        private static Bitmap CreateMenuImage(Icon source, List<Bitmap> menuImages)
        {
            using (Icon icon = new Icon(source, SystemInformation.SmallIconSize))
            {
                Bitmap image = icon.ToBitmap();
                menuImages.Add(image);
                return image;
            }
        }

        private void RefreshExtensionCacheFromSelectedItems()
        {
            // 每种输入扩展名只统计一次，保留原有的小写匹配规则。
            this.extensionCache.Clear();
            foreach (string filePath in this.SelectedItemPaths)
            {
                string extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension))
                {
                    continue;
                }

                extension = extension.Substring(1).ToLowerInvariant();

                this.extensionCache.Add(extension);
            }
        }

        private void RefreshPresetList()
        {
            this.RefreshExtensionCacheFromSelectedItems();
            this.LoadExtensionSettingsIfNecessary();

            this.menuEntries.Clear();
            this.menuEntriesByName.Clear();
            MenuEntry unnamedEntry = null;
            foreach (string extension in this.extensionCache)
            {
                if (!this.presetsByExtension.TryGetValue(extension, out List<PresetReference> presets))
                {
                    continue;
                }

                foreach (PresetReference presetReference in presets)
                {
                    MenuEntry menuEntry;
                    if (presetReference.FullName == null)
                    {
                        menuEntry = unnamedEntry;
                    }
                    else
                    {
                        this.menuEntriesByName.TryGetValue(presetReference.FullName, out menuEntry);
                    }

                    if (menuEntry == null)
                    {
                        menuEntry = new MenuEntry(presetReference);
                        this.menuEntries.Add(menuEntry);
                        if (presetReference.FullName == null)
                        {
                            unnamedEntry = menuEntry;
                        }
                        else
                        {
                            this.menuEntriesByName.Add(presetReference.FullName, menuEntry);
                        }
                    }

                    menuEntry.ExtensionRefCount++;
                }
            }

            // 保持原有菜单顺序及兼容所有输入扩展名时才启用的规则。
            foreach (MenuEntry menuEntry in this.menuEntries)
            {
                menuEntry.Enabled = menuEntry.ExtensionRefCount == this.extensionCache.Count;
            }
        }

        private void LoadExtensionSettingsIfNecessary()
        {
            if (this.presetReferences != null)
            {
                return;
            }

            if (File.Exists(PathHelpers.UserSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile("Settings", PathHelpers.UserSettingsFilePath, out this.presetReferences);
                }
                catch
                {
                    // 用户配置损坏时继续尝试默认配置，避免影响资源管理器。
                }
            }

            if (this.presetReferences == null)
            {
                try
                {
                    XmlHelpers.LoadFromFile("Settings", PathHelpers.DefaultSettingsFilePath, out this.presetReferences);
                }
                catch
                {
                    // 配置不可用时隐藏转换菜单，避免在资源管理器中抛出异常。
                    this.presetReferences = Array.Empty<PresetReference>();
                }
            }

            this.BuildPresetIndex();
        }

        private void BuildPresetIndex()
        {
            this.presetsByExtension.Clear();
            foreach (PresetReference presetReference in this.presetReferences)
            {
                if (presetReference?.InputTypes == null)
                {
                    continue;
                }

                foreach (string extension in presetReference.InputTypes)
                {
                    if (string.IsNullOrEmpty(extension))
                    {
                        continue;
                    }

                    if (!this.presetsByExtension.TryGetValue(extension, out List<PresetReference> presets))
                    {
                        presets = new List<PresetReference>();
                        this.presetsByExtension.Add(extension, presets);
                    }

                    // 原有 Contains 只匹配一次，重复的 InputTypes 不应重复计数。
                    if (presets.Count == 0 || presets[presets.Count - 1] != presetReference)
                    {
                        presets.Add(presetReference);
                    }
                }
            }
        }

        private void OpenSettings()
        {
            if (!TryGetExecutablePath(out string executablePath))
            {
                return;
            }

            ProcessStartInfo processStartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                Arguments = "--settings",
            };

            using (Process.Start(processStartInfo))
            {
            }
        }

        private void ConvertFiles(string presetName)
        {
            if (!TryGetExecutablePath(out string executablePath))
            {
                return;
            }

            void BuildConversionPresetArgument(StringBuilder sb)
            {
                sb.Append("--conversion-preset ");
                sb.Append(" \"");
                sb.Append(presetName);
                sb.Append("\"");
            }

            // 保留现有参数协议，过长的文件列表通过临时文件传递。
            StringBuilder stringBuilder = new StringBuilder();
            BuildConversionPresetArgument(stringBuilder);

            string fileListPath = null;
            foreach (var filePath in this.SelectedItemPaths)
            {
                stringBuilder.Append(" \"");
                stringBuilder.Append(filePath);
                stringBuilder.Append("\"");

                if (stringBuilder.Length >= MaximumProcessArgumentsLength)
                {
                    stringBuilder.Clear();
                    BuildConversionPresetArgument(stringBuilder);

                    // 使用独立名称并原子创建，避免同时启动转换时覆盖其他文件列表。
                    fileListPath = Path.Combine(Path.GetTempPath(), $"file-converter-input-list-{Guid.NewGuid():N}.txt");

                    try
                    {
                        using (FileStream file = new FileStream(fileListPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (StreamWriter writer = new StreamWriter(file))
                        {
                            foreach (var path in this.SelectedItemPaths)
                            {
                                writer.WriteLine(path);
                            }
                        }
                    }
                    catch
                    {
                        DeleteInputFileList(fileListPath);
                        throw;
                    }

                    stringBuilder.Append(" --input-files ");
                    stringBuilder.Append(" \"");
                    stringBuilder.Append(fileListPath);
                    stringBuilder.Append("\"");
                    break;
                }
            }

            var processStartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                Arguments = stringBuilder.ToString(),
            };

            if (fileListPath == null)
            {
                using (Process.Start(processStartInfo))
                {
                }

                return;
            }

            Process exeProcess = new Process { StartInfo = processStartInfo };
            // 在启动前订阅退出事件，确保快速退出的进程也能清理文件和句柄。
            exeProcess.Exited += (sender, args) =>
            {
                DeleteInputFileList(fileListPath);
                ((Process)sender).Dispose();
            };
            exeProcess.EnableRaisingEvents = true;
            try
            {
                if (!exeProcess.Start())
                {
                    DeleteInputFileList(fileListPath);
                    exeProcess.Dispose();
                }
            }
            catch
            {
                DeleteInputFileList(fileListPath);
                exeProcess.Dispose();
                throw;
            }
        }

        private static string GetText(string name) => Properties.Resources.ResourceManager.GetString(name, Properties.Resources.Culture);

        private static bool TryGetExecutablePath(out string executablePath)
        {
            executablePath = PathHelpers.FileConverterPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                MessageBox.Show(GetText("InvalidExecutablePathMessage"));
                return false;
            }

            if (!File.Exists(executablePath))
            {
                MessageBox.Show(string.Format(GetText("ExecutableNotFoundMessage"), executablePath));
                return false;
            }

            return true;
        }

        private static void DeleteInputFileList(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 临时文件被占用时不影响转换结果或资源管理器。
            }
            catch (UnauthorizedAccessException)
            {
                // 临时目录权限变化时仍确保调用方能够释放进程句柄。
            }
        }
    }
}
