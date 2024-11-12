using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SourceButler.Models
{
    public class FileTreeItem : INotifyPropertyChanged
    {
        private bool isSelected;
        private readonly Action<FileTreeItem, bool>? onSelectionChanged;
        private bool isExpanded = true;

        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public ObservableCollection<FileTreeItem> Children { get; } = new();
        public bool IsDirectory { get; set; }

        public FileTreeItem(Action<FileTreeItem, bool>? onSelectionChanged = null)
        {
            this.onSelectionChanged = onSelectionChanged;
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));

                    // Propagate selection to children if this is a directory
                    if (IsDirectory)
                    {
                        foreach (var child in Children)
                        {
                            child.IsSelected = value;
                        }
                    }

                    // Notify about the selection change
                    onSelectionChanged?.Invoke(this, value);
                }
            }
        }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded != value)
                {
                    isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}