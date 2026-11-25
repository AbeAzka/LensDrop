using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LensDrop.Models
{
    public class CameraMediaItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _status = "Siap Export";
        private string _statusCategory = "Ready"; // "Ready", "Processing", "Done", "Error"
        private string _customFolderTag = string.Empty;
        private PhotoEditSettings? _editSettings;
        private string? _localCachedPath;

        public string RemotePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty; // "Image" atau "Video"
        public long FileSizeBytes { get; set; }
        public DateTime? DateTaken { get; set; }

        public bool IsEditableImage
        {
            get
            {
                string ext = Extension.ToLowerInvariant();
                return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
            }
        }

        public string? LocalCachedPath
        {
            get => _localCachedPath;
            set { _localCachedPath = value; OnPropertyChanged(); }
        }

        public PhotoEditSettings? EditSettings
        {
            get => _editSettings;
            set
            {
                _editSettings = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EditBadgeText));
                OnPropertyChanged(nameof(HasPendingEdits));
            }
        }

        public bool HasPendingEdits => _editSettings?.HasEdits == true;

        public string EditBadgeText => HasPendingEdits
            ? $"Edited ({_editSettings!.PresetName})"
            : (IsEditableImage ? "Asli" : "RAW/Video");

        public string CustomFolderTag
        {
            get => _customFolderTag;
            set { _customFolderTag = value; OnPropertyChanged(); }
        }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSizeBytes >= 1024L * 1024L * 1024L)
                    return $"{(FileSizeBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
                if (FileSizeBytes >= 1024L * 1024L)
                    return $"{(FileSizeBytes / (1024.0 * 1024.0)):F2} MB";
                return $"{(FileSizeBytes / 1024.0):F0} KB";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string StatusCategory
        {
            get => _statusCategory;
            set { _statusCategory = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
