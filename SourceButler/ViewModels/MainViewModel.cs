using Microsoft.Win32;
using SourceButler.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Text;

namespace SourceButler.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string rootDirectory = string.Empty;
        private double scanProgress;
        private double processProgress;
        private string logText = string.Empty;
        private bool isProcessing;
        private Configuration? currentConfig;
        private StringBuilder outputBuilder = new();
        public static readonly string[] ExcludedFolders = new[] { ".git", ".github" };
        private readonly Dictionary<string, HashSet<string>> extensionToFolderMap = new();

        public ObservableCollection<FileTreeItem> FolderTree { get; } = new();
        private ObservableCollection<ExtensionItem> fileExtensions = new();
        public ObservableCollection<ExtensionItem> FileExtensions
        {
            get => fileExtensions;
            set
            {
                fileExtensions = value;
                OnPropertyChanged(nameof(FileExtensions));
            }
        }
        private HashSet<string> selectedExtensions = new();
        public HashSet<string> SelectedExtensions
        {
            get => selectedExtensions;
            set
            {
                selectedExtensions = value;
                OnPropertyChanged(nameof(SelectedExtensions));
            }
        }

        public ICommand SelectFolderCommand { get; }
        public ICommand ProcessCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ToggleExtensionCommand { get; }
        public string RootDirectory
        {
            get => rootDirectory;
            set
            {
                if (rootDirectory != value)
                {
                    rootDirectory = value;
                    OnPropertyChanged(nameof(RootDirectory));
                }
            }
        }

        public double ScanProgress
        {
            get => scanProgress;
            set
            {
                scanProgress = value;
                OnPropertyChanged(nameof(ScanProgress));
            }
        }

        public double ProcessProgress
        {
            get => processProgress;
            set
            {
                processProgress = value;
                OnPropertyChanged(nameof(ProcessProgress));
            }
        }

        public string LogText
        {
            get => logText;
            set
            {
                logText = value;
                OnPropertyChanged(nameof(LogText));
            }
        }

        public bool IsProcessing
        {
            get => isProcessing;
            set
            {
                isProcessing = value;
                OnPropertyChanged(nameof(IsProcessing));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public MainViewModel()
        {
            SelectFolderCommand = new RelayCommand(SelectFolder);
            ProcessCommand = new RelayCommand(ProcessFiles, CanProcessFiles);
            ClearLogCommand = new RelayCommand(ClearLog);
            ToggleExtensionCommand = new RelayCommand<string>(ToggleExtension);
        }

        private void SelectFolder()
        {
            var folderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Source Directory"
            };

            if (folderDialog.ShowDialog() == true)
            {
                // Clear existing data
                FolderTree.Clear();
                FileExtensions.Clear();
                extensionToFolderMap.Clear();

                // Set directory and load configuration in sequence
                RootDirectory = folderDialog.FolderName;
                LoadConfiguration();
            }
        }

        private void LoadConfiguration()
        {
            LogMessage("Starting configuration load...");
            var configPath = Path.Combine(RootDirectory, Configuration.ConfigFileName);
            LogMessage($"Looking for config at: {configPath}");

            currentConfig = Configuration.LoadFromFile(configPath);
            LogMessage($"Config loaded: {(currentConfig != null ? "Yes" : "No")}");

            if (currentConfig != null)
            {
                LogMessage($"Last root directory: {currentConfig.LastRootDirectory}");
                LogMessage($"Selected folders count: {currentConfig.SelectedFolders?.Count ?? 0}");
                LogMessage($"Selected extensions count: {currentConfig.SelectedExtensions?.Count ?? 0}");

                if (currentConfig.SelectedFolders?.Any() == true)
                {
                    LogMessage("First few selected folders:");
                    foreach (var folder in currentConfig.SelectedFolders.Take(3))
                    {
                        LogMessage($"- {folder}");
                    }
                }
            }

            // Initialize SelectedExtensions from config
            if (currentConfig?.SelectedExtensions != null)
            {
                SelectedExtensions.Clear();
                foreach (var ext in currentConfig.SelectedExtensions)
                {
                    SelectedExtensions.Add(ext);
                    LogMessage($"Added extension from config: {ext}");
                }
            }

            LoadFolderStructure();
        }

        private async void LoadFolderStructure()
        {
            IsProcessing = true;
            FolderTree.Clear();
            FileExtensions.Clear();
            extensionToFolderMap.Clear();

            try
            {
                var rootItem = await Task.Run(() => ScanDirectory(new DirectoryInfo(RootDirectory)));
                SetInitialTreeState(rootItem);
                FolderTree.Add(rootItem);

                // Single UI update for extensions
                Application.Current.Dispatcher.Invoke(() => UpdateExtensionsList());
            }
            catch (Exception ex)
            {
                LogMessage($"Error: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // In UpdateExtensionsList method, modify the extension creation part
        private void UpdateExtensionsList()
        {
            var selectedFolders = GetSelectedFolderPaths();
            var extensionsToShow = new Dictionary<string, int>();

            foreach (var mapping in extensionToFolderMap)
            {
                var selectedFolderCount = mapping.Value.Count(folder => selectedFolders.Contains(folder));
                if (selectedFolderCount > 0)
                {
                    extensionsToShow[mapping.Key] = selectedFolderCount;
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var currentExtensions = FileExtensions.ToDictionary(ei => ei.Extension);

                // Remove non-present extensions
                foreach (var ei in FileExtensions.ToList())
                {
                    if (!extensionsToShow.ContainsKey(ei.Extension))
                    {
                        FileExtensions.Remove(ei);
                        // Only remove from SelectedExtensions if it's not in any selected folder
                        if (!extensionToFolderMap[ei.Extension]
                            .Any(folder => selectedFolders.Contains(folder)))
                        {
                            SelectedExtensions.Remove(ei.Extension);
                        }
                    }
                }

                // Add or update extensions
                foreach (var ext in extensionsToShow)
                {
                    if (currentExtensions.TryGetValue(ext.Key, out var existingItem))
                    {
                        existingItem.SelectedFolderCount = ext.Value;
                        existingItem.IsChecked = SelectedExtensions.Contains(ext.Key);
                    }
                    else
                    {
                        // Add to SelectedExtensions by default if it's not already there
                        if (!SelectedExtensions.Contains(ext.Key))
                        {
                            SelectedExtensions.Add(ext.Key);
                        }

                        FileExtensions.Add(new ExtensionItem
                        {
                            Extension = ext.Key,
                            SelectedFolderCount = ext.Value,
                            IsChecked = true  // Default to checked
                        });
                    }
                }
            });
        }

        private void SetInitialTreeState(FileTreeItem item)
        {
            // Set state for current item
            item.IsSelected = true;
            item.IsExpanded = true;

            // Recursively set state for all children
            foreach (var child in item.Children)
            {
                SetInitialTreeState(child);
            }
        }

        private FileTreeItem ScanDirectory(DirectoryInfo directory)
        {
            // Changed logic for determining initial selection state
            bool shouldSelect;
            if (currentConfig == null)
            {
                shouldSelect = currentConfig.SelectedFolders?.Contains(directory.FullName) ?? false;
                LogMessage($"Scanning directory: {directory.FullName}, Should select: {shouldSelect}");
                //shouldSelect = true;
            }
            else if (currentConfig.LastRootDirectory != RootDirectory)
            {
                // Different root directory - select everything
                shouldSelect = true;
            }
            else
            {
                // Use configuration
                shouldSelect = currentConfig.SelectedFolders.Contains(directory.FullName);
            }

            var item = new FileTreeItem(HandleFolderSelectionChanged)
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                IsSelected = shouldSelect,
                IsExpanded = shouldSelect
            };

            try
            {
                foreach (var dir in directory.GetDirectories()
                    .Where(dir => !ExcludedFolders.Contains(dir.Name)))
                {
                    var childItem = ScanDirectory(dir);
                    item.Children.Add(childItem);

                    // Don't override child selection state from config
                    if (shouldSelect && currentConfig == null)
                    {
                        childItem.IsSelected = true;
                    }
                }

                foreach (var file in directory.GetFiles())
                {
                    var extension = file.Extension.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(extension))
                    {
                        if (!extensionToFolderMap.ContainsKey(extension))
                        {
                            extensionToFolderMap[extension] = new HashSet<string>();
                        }
                        extensionToFolderMap[extension].Add(directory.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error scanning {directory.FullName}: {ex.Message}");
            }

            return item;
        }

        private void HandleFolderSelectionChanged(FileTreeItem item, bool isSelected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateExtensionsList();
                // We no longer clear selections here as it's handled in UpdateExtensionsList
            });
        }
        private void ToggleExtension(string extension)
        {
            if (extension == null) return;

            if (SelectedExtensions.Contains(extension))
            {
                SelectedExtensions.Remove(extension);
            }
            else
            {
                SelectedExtensions.Add(extension);
            }

            // Update the UI to reflect the change
            var extensionItem = FileExtensions.FirstOrDefault(ei => ei.Extension == extension);
            if (extensionItem != null)
            {
                extensionItem.IsChecked = SelectedExtensions.Contains(extension);
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateScanProgress()
        {
            // For now, this is a placeholder.
            // We'll implement proper progress tracking later
            ScanProgress += 1;
        }

        private void LogMessage(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogText += $"{DateTime.Now:HH:mm:ss}: {message}\n";
            });
        }

        private void ClearLog()
        {
            LogText = string.Empty;
        }

        private bool CanProcessFiles()
        {
            return !string.IsNullOrEmpty(RootDirectory) &&
                   SelectedExtensions.Any() &&
                   !IsProcessing;
        }

        private async void ProcessFiles()
        {
            IsProcessing = true;
            try
            {
                // Save configuration
                var configPath = Path.Combine(RootDirectory, Configuration.ConfigFileName);
                var config = new Configuration
                {
                    LastRootDirectory = RootDirectory,
                    SelectedExtensions = SelectedExtensions.ToList(),
                    SelectedFolders = GetSelectedFolderPaths()
                };
                config.SaveToFile(configPath);

                // Clear the StringBuilder
                outputBuilder.Clear();
                var selectedPaths = GetSelectedFolderPaths();

                // Generate folder structure
                LogMessage("Generating folder structure...");
                await Task.Run(() =>
                {
                    GenerateFolderStructure(outputBuilder, RootDirectory, selectedPaths, "");
                });

                // Process files
                LogMessage("Processing files...");
                var processedFiles = await ProcessSelectedFiles(selectedPaths);

                // Show save dialog
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt",
                    Title = "Save Output File",
                    FileName = "source_output.txt",
                    DefaultExt = ".txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await File.WriteAllTextAsync(saveDialog.FileName, outputBuilder.ToString());
                    LogMessage($"Output saved to: {saveDialog.FileName}");
                }

                LogMessage($"Processing completed! Processed {processedFiles} files.");
            }
            catch (Exception ex)
            {
                LogMessage($"Error during processing: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void GenerateFolderStructure(StringBuilder builder, string currentPath, List<string> selectedPaths, string indent, bool isRoot = true)
        {
            var dirInfo = new DirectoryInfo(currentPath);

            // Only process this directory if it's the root or is selected
            if (currentPath == RootDirectory || selectedPaths.Contains(currentPath))
            {
                // Add this directory to the output only if it's the root
                if (isRoot)
                {
                    builder.AppendLine($"{indent}{dirInfo.Name}");
                }

                // Get all items to process (files and directories)
                var files = dirInfo.GetFiles()
                    .Where(f => SelectedExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .Select(f => (IsDirectory: false, Name: f.Name, Path: f.FullName))
                    .ToList();

                var subdirs = dirInfo.GetDirectories()
                    .Where(d => selectedPaths.Contains(d.FullName))
                    .Select(d => (IsDirectory: true, Name: d.Name, Path: d.FullName))
                    .ToList();

                // Combine and order all items
                var allItems = files.Concat(subdirs).OrderBy(x => x.Name).ToList();

                // Process each item
                for (int i = 0; i < allItems.Count; i++)
                {
                    var item = allItems[i];
                    var isLast = i == allItems.Count - 1;
                    var prefix = isLast ? "└── " : "├── ";

                    if (item.IsDirectory)
                    {
                        // For directories, recursively process them
                        builder.AppendLine($"{indent}{prefix}{item.Name}");
                        var newIndent = indent + (isLast ? "    " : "│   ");
                        GenerateFolderStructure(builder, item.Path, selectedPaths, newIndent, false);
                    }
                    else
                    {
                        // For files, just add them to the output
                        builder.AppendLine($"{indent}{prefix}{item.Name}");
                    }
                }
            }
        }

        private async Task<int> ProcessSelectedFiles(List<string> selectedPaths)
        {
            var processedFiles = 0;
            ProcessProgress = 0;

            try
            {
                // First generate the folder structure
                outputBuilder.Clear();
                await Task.Run(() => GenerateFolderStructure(outputBuilder, RootDirectory, selectedPaths, "", true));

                // Now process only the files in selected folders
                var filesToProcess = new List<string>();
                foreach (var path in selectedPaths)
                {
                    // Only get files directly in the selected directory
                    filesToProcess.AddRange(
                        Directory.GetFiles(path)
                        .Where(f => SelectedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    );
                }

                var totalFiles = filesToProcess.Count;
                LogMessage($"Found {totalFiles} files to process in selected folders.");

                // Process each file
                foreach (var file in filesToProcess)
                {
                    if (await ProcessFileIfSelected(file))
                    {
                        processedFiles++;
                        ProcessProgress = (processedFiles * 100.0) / totalFiles;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error during file processing: {ex.Message}");
            }

            return processedFiles;
        }

        private int GetTotalFilesToProcess(List<string> selectedPaths)
        {
            return selectedPaths.Sum(path =>
                Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)  // Changed from AllFiles to AllDirectories
                    .Count(file => SelectedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())));
        }

        private async Task<bool> ProcessFileIfSelected(string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            try
            {
                if (fileInfo.Length > Configuration.MaxFileSize)
                {
                    LogMessage($"Skipping large file: {filePath}");
                    return false;
                }

                if (IsBinaryFile(filePath))
                {
                    LogMessage($"Skipping binary file: {filePath}");
                    return false;
                }

                using var reader = new StreamReader(filePath);
                var content = await reader.ReadToEndAsync();
                outputBuilder.AppendLine($"\n{filePath}:\n{content}\n");  // Removed <code> tags
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"Error processing file {filePath}: {ex.Message}");
                return false;
            }
        }

        private bool IsBinaryFile(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var buffer = new byte[Math.Min(stream.Length, 1024)];
                stream.Read(buffer, 0, buffer.Length);

                return buffer.Any(b => b == 0);
            }
            catch
            {
                return true; // If we can't read the file, assume it's binary
            }
        }

        private List<string> GetSelectedFolderPaths()
        {
            var paths = new List<string>();
            if (FolderTree.Any())
            {
                CollectSelectedPaths(FolderTree[0], paths);
            }
            return paths;
        }

        private void CollectSelectedPaths(FileTreeItem item, List<string> paths)
        {
            if (item.IsSelected)
            {
                paths.Add(item.FullPath);
            }

            foreach (var child in item.Children)
            {
                // Only recurse into children if parent is selected
                if (item.IsSelected)
                {
                    CollectSelectedPaths(child, paths);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int CountFilesToProcess(List<string> selectedPaths)
        {
            int count = 0;
            foreach (var selectedPath in selectedPaths)
            {
                // Count files in the current directory
                count += Directory.GetFiles(selectedPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(file => SelectedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));

                // Count files in selected subdirectories
                foreach (var subDir in Directory.GetDirectories(selectedPath, "*", SearchOption.TopDirectoryOnly))
                {
                    if (selectedPaths.Contains(subDir))
                    {
                        count += Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories)
                            .Count(file => SelectedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
                    }
                }
            }
            return count;
        }
    }

public class ExtensionItem : INotifyPropertyChanged
{
    private string extension = string.Empty;
    private int selectedFolderCount;
    private bool isChecked;

    public string Extension
    {
        get => extension;
        set
        {
            extension = value;
            OnPropertyChanged(nameof(Extension));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public int SelectedFolderCount
    {
        get => selectedFolderCount;
        set
        {
            selectedFolderCount = value;
            OnPropertyChanged(nameof(SelectedFolderCount));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    public string DisplayText => $"{Extension} ({SelectedFolderCount} selected)";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
}

