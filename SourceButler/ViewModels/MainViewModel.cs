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
using System.Collections.Concurrent;

namespace SourceButler.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string rootDirectory = string.Empty;
        private int totalFoldersToScan;
        private int foldersScanned;
        private double scanProgress;
        private readonly ConcurrentDictionary<string, bool> processedPaths = new();
        private double processProgress;
        private string logText = string.Empty;
        private bool isProcessing;
        private Configuration? currentConfig;
        private StringBuilder outputBuilder = new();
        public static readonly string[] ExcludedFolders = new[] { ".git", ".github", "node_modules" };
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

                // Initialize SelectedExtensions from config before loading folder structure
                SelectedExtensions = new HashSet<string>();
                if (currentConfig?.SelectedExtensions != null)
                {
                    foreach (var ext in currentConfig.SelectedExtensions)
                    {
                        SelectedExtensions.Add(ext.ToLowerInvariant());
                        LogMessage($"Added extension from config: {ext}");
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
            processedPaths.Clear();
            ScanProgress = 0;

            try
            {
                // First pass: Count total folders
                totalFoldersToScan = await Task.Run(() => CountFoldersToScan(RootDirectory));
                foldersScanned = 0;
                LogMessage($"Found {totalFoldersToScan} folders to scan");

                // Second pass: Actual scanning
                var rootItem = await Task.Run(() => ScanDirectory(new DirectoryInfo(RootDirectory)));

                Application.Current.Dispatcher.Invoke(() =>
                {
                    SetInitialTreeState(rootItem);
                    FolderTree.Add(rootItem);
                    UpdateExtensionsList();
                    ScanProgress = 100; // Ensure we end at 100%
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error during folder scan: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private int CountFoldersToScan(string rootPath)
        {
            try
            {
                var count = 1; // Count current directory
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    if (!ExcludedFolders.Contains(Path.GetFileName(dir)))
                    {
                        count += CountFoldersToScan(dir);
                    }
                }
                return count;
            }
            catch (Exception ex)
            {
                LogMessage($"Error counting folders in {rootPath}: {ex.Message}");
                return 0;
            }
        }

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

                // Remove extensions no longer present
                foreach (var ei in FileExtensions.ToList())
                {
                    if (!extensionsToShow.ContainsKey(ei.Extension))
                    {
                        FileExtensions.Remove(ei);
                    }
                }

                // Add or update extensions
                foreach (var ext in extensionsToShow)
                {
                    if (currentExtensions.TryGetValue(ext.Key, out var existingItem))
                    {
                        // Update existing item
                        existingItem.SelectedFolderCount = ext.Value;
                        existingItem.IsChecked = SelectedExtensions.Contains(ext.Key);
                    }
                    else
                    {
                        // Create new item with correct checked state
                        var isChecked = false;
                        if (currentConfig?.SelectedExtensions != null)
                        {
                            // If we have a config, use it to determine checked state
                            isChecked = currentConfig.SelectedExtensions.Contains(ext.Key);
                        }
                        else
                        {
                            // If no config, don't auto-select extensions
                            isChecked = false;
                        }

                        // Ensure SelectedExtensions is synchronized
                        if (isChecked)
                        {
                            SelectedExtensions.Add(ext.Key);
                        }
                        else
                        {
                            SelectedExtensions.Remove(ext.Key);
                        }

                        FileExtensions.Add(new ExtensionItem
                        {
                            Extension = ext.Key,
                            SelectedFolderCount = ext.Value,
                            IsChecked = isChecked
                        });
                    }
                }

                var sortedExtensions = FileExtensions.OrderBy(x => x.Extension).ToList();
                FileExtensions.Clear();
                foreach (var ext in sortedExtensions)
                {
                    FileExtensions.Add(ext);
                }
            });
        }

        private void SetInitialTreeState(FileTreeItem item)
        {
            // Set state for current item
            item.IsExpanded = true;

            // Recursively set state for all children
            foreach (var child in item.Children)
            {
                SetInitialTreeState(child);
            }
        }

        private FileTreeItem ScanDirectory(DirectoryInfo directory)
        {
            if (!processedPaths.TryAdd(directory.FullName, true))
            {
                return new FileTreeItem(HandleFolderSelectionChanged)
                {
                    Name = directory.Name,
                    FullPath = directory.FullName,
                    IsDirectory = true
                };
            }

            bool shouldSelect = false;

            if (currentConfig != null)
            {
                if (currentConfig.LastRootDirectory != RootDirectory)
                {
                    shouldSelect = false;
                }
                else
                {
                    shouldSelect = currentConfig.SelectedFolders.Contains(directory.FullName);
                    //LogMessage($"Checking directory: {directory.FullName}, Selected: {shouldSelect}");
                }
            }

            var item = new FileTreeItem(HandleFolderSelectionChanged)
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                IsSelected = shouldSelect,
                IsExpanded = true
            };

            try
            {
                // Process all directories first
                foreach (var dir in directory.GetDirectories()
                    .Where(dir => !ExcludedFolders.Contains(dir.Name)))
                {
                    var childItem = ScanDirectory(dir);
                    item.Children.Add(childItem);
                }

                // Process files in current directory
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

                // Update progress after processing each directory
                Interlocked.Increment(ref foldersScanned);
                UpdateScanProgress();
            }
            catch (Exception ex)
            {
                LogMessage($"Error scanning {directory.FullName}: {ex.Message}");
            }

            return item;
        }

        private void UpdateScanProgress()
        {
            if (totalFoldersToScan > 0)
            {
                var progress = (foldersScanned * 100.0) / totalFoldersToScan;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScanProgress = Math.Min(99, progress); // Cap at 99% until completely done
                });
            }
        }

        private void HandleFolderSelectionChanged(FileTreeItem item, bool isSelected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateExtensionsList();
            });
        }
        private void ToggleExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return;

            var normalizedExtension = extension.ToLowerInvariant();

            if (SelectedExtensions.Contains(normalizedExtension))
            {
                SelectedExtensions.Remove(normalizedExtension);
            }
            else
            {
                SelectedExtensions.Add(normalizedExtension);
            }

            // Update the UI to reflect the change
            var extensionItem = FileExtensions.FirstOrDefault(ei =>
                ei.Extension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

            if (extensionItem != null)
            {
                extensionItem.IsChecked = SelectedExtensions.Contains(normalizedExtension);
            }

            CommandManager.InvalidateRequerySuggested();
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
                outputBuilder.Clear();
                await Task.Run(() => GenerateFolderStructure(outputBuilder, RootDirectory, selectedPaths, "", true));

                var filesToProcess = selectedPaths
                    .SelectMany(path => Directory.GetFiles(path))
                    .Where(f => SelectedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                var totalFiles = filesToProcess.Count;
                LogMessage($"Found {totalFiles} files to process in selected folders.");

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
                outputBuilder.AppendLine($"\n{filePath}:\n{content}\n");
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
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                return buffer.Take(bytesRead).Any(b => b == 0);
            }
            catch (Exception ex)
            {
                LogMessage($"Error checking if file is binary {filePath}: {ex.Message}");
                return true;
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
                    if (extension != value)
                    {
                        extension = value.ToLowerInvariant(); // Ensure consistent casing
                        OnPropertyChanged(nameof(Extension));
                        OnPropertyChanged(nameof(DisplayText));
                    }
                }
            }

            public int SelectedFolderCount
            {
                get => selectedFolderCount;
                set
                {
                    if (selectedFolderCount != value)
                    {
                        selectedFolderCount = value;
                        OnPropertyChanged(nameof(SelectedFolderCount));
                        OnPropertyChanged(nameof(DisplayText));
                    }
                }
            }

            public bool IsChecked
            {
                get => isChecked;
                set
                {
                    if (isChecked != value)
                    {
                        isChecked = value;
                        OnPropertyChanged(nameof(IsChecked));
                    }
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

}