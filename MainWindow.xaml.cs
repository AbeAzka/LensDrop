using LensDrop.Models;
using LensDrop.Services;
using LensDrop.Views;
using MediaDevices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LensDrop
{
    public class AppUserPreferences
    {
        public bool AutoLoginGoogle { get; set; } = true;
        public string OutputDirectory { get; set; } = string.Empty;
        public string DriveRootFolder { get; set; } = "LensDrop_Backup";
        public bool SeparateType { get; set; } = true;
        public bool GroupDate { get; set; } = true;
        public bool GroupModel { get; set; } = false;
        public bool RenameTimestamp { get; set; } = false;
    }

    public partial class MainWindow : Window
    {
        private readonly List<ICameraSource> _availableSources = new();
        private readonly WifiCameraDiscoveryService _wifiDiscovery = new();
        private readonly MediaSorterService _sorterService = new();
        private readonly GoogleDriveSyncService _driveService = new();
        private readonly PhotoEditorService _photoEditorService = new();

        private readonly ObservableCollection<CameraMediaItem> _mediaItems = new();
        private ICollectionView? _mediaView;
        private AppUserPreferences _prefs = new();
        private bool _isUiReady = false;

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;

        private static string PrefsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LensDrop",
            "preferences.json"
        );

        public MainWindow()
        {
            InitializeComponent();
            LoadPreferences();

            _mediaView = CollectionViewSource.GetDefaultView(_mediaItems);
            _mediaView.Filter = FilterMediaPredicate;
            GridMedia.ItemsSource = _mediaView;

            _isUiReady = true;
            UpdateFolderPreview();

            Loaded += async (_, _) =>
            {
                await AutoLoginGoogleIfEnabledAsync();
                await RefreshCamerasAndScanAsync();
            };
        }

        private void LoadPreferences()
        {
            try
            {
                if (File.Exists(PrefsFilePath))
                {
                    string json = File.ReadAllText(PrefsFilePath);
                    _prefs = JsonSerializer.Deserialize<AppUserPreferences>(json) ?? new AppUserPreferences();
                }
            }
            catch
            {
                _prefs = new AppUserPreferences();
            }

            if (string.IsNullOrWhiteSpace(_prefs.OutputDirectory))
            {
                _prefs.OutputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "LensDrop"
                );
            }

            ChkAutoLoginDrive.IsChecked = _prefs.AutoLoginGoogle;
            TxtOutputDir.Text = _prefs.OutputDirectory;
            TxtDriveRootFolder.Text = _prefs.DriveRootFolder;
            ChkSeparateType.IsChecked = _prefs.SeparateType;
            ChkGroupDate.IsChecked = _prefs.GroupDate;
            ChkGroupModel.IsChecked = _prefs.GroupModel;
            ChkRenameTimestamp.IsChecked = _prefs.RenameTimestamp;
        }

        private void SavePreferences()
        {
            if (!_isUiReady) return;
            try
            {
                _prefs.AutoLoginGoogle = ChkAutoLoginDrive.IsChecked == true;
                _prefs.OutputDirectory = TxtOutputDir.Text;
                _prefs.DriveRootFolder = TxtDriveRootFolder.Text;
                _prefs.SeparateType = ChkSeparateType.IsChecked == true;
                _prefs.GroupDate = ChkGroupDate.IsChecked == true;
                _prefs.GroupModel = ChkGroupModel.IsChecked == true;
                _prefs.RenameTimestamp = ChkRenameTimestamp.IsChecked == true;

                Directory.CreateDirectory(Path.GetDirectoryName(PrefsFilePath)!);
                File.WriteAllText(PrefsFilePath, JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVICEARRIVAL)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    TxtLog.Text = "Perangkat USB baru terdeteksi, memindai kamera...";
                    await RefreshCamerasAndScanAsync();
                });
            }
            return IntPtr.Zero;
        }

        // ==================== GOOGLE AUTO-LOGIN & PROFILE ====================

        private async Task AutoLoginGoogleIfEnabledAsync()
        {
            if (ChkAutoLoginDrive.IsChecked != true || !_driveService.HasSavedToken())
                return;

            TxtProfileName.Text = "Memulihkan sesi Google...";
            TxtProfileEmail.Text = "Auto-login ke Drive & Profil";
            BtnLoginDrive.IsEnabled = false;

            bool restored = await _driveService.TrySilentAuthenticateAsync("credentials.json");
            SetGoogleProfileUi(restored);
        }

        private void SetGoogleProfileUi(bool isConnected)
        {
            if (isConnected && _driveService.CurrentUser != null)
            {
                var user = _driveService.CurrentUser;
                TxtProfileName.Text = user.Name;
                TxtProfileEmail.Text = string.IsNullOrWhiteSpace(user.Email) ? "Google Drive Aktif" : user.Email;
                TxtAvatarInitials.Text = user.Initials;
                AvatarBg.Fill = new SolidColorBrush(Color.FromRgb(5, 150, 105));

                if (!string.IsNullOrWhiteSpace(user.PictureUrl))
                {
                    try
                    {
                        var bmp = new BitmapImage(new Uri(user.PictureUrl, UriKind.Absolute));
                        BrushAvatarImage.ImageSource = bmp;
                        AvatarImageEllipse.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        AvatarImageEllipse.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    AvatarImageEllipse.Visibility = Visibility.Collapsed;
                }

                BadgeDriveStatus.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgeDriveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));

                BtnLoginDrive.Content = "Logout";
                BtnLoginDrive.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                BtnLoginDrive.IsEnabled = true;
            }
            else
            {
                TxtProfileName.Text = "Akun Google Belum Login";
                TxtProfileEmail.Text = "Klik Login Google untuk profil & sync Drive";
                TxtAvatarInitials.Text = "G";
                AvatarBg.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                AvatarImageEllipse.Visibility = Visibility.Collapsed;

                BadgeDriveStatus.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                BadgeDriveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));

                BtnLoginDrive.Content = "Login dengan Google";
                BtnLoginDrive.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                BtnLoginDrive.IsEnabled = true;
            }
        }

        private async void BtnLoginDrive_Click(object sender, RoutedEventArgs e)
        {
            if (_driveService.IsAuthenticated)
            {
                var confirm = MessageBox.Show(
                    $"Logout dari akun {_driveService.CurrentUser?.Email}?",
                    "Logout Akun Google",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                if (confirm != MessageBoxResult.Yes) return;

                await _driveService.LogoutAsync();
                SetGoogleProfileUi(false);
                TxtLog.Text = "Berhasil logout dari akun Google.";
                return;
            }

            BtnLoginDrive.IsEnabled = false;
            TxtProfileName.Text = "Membuka login browser...";
            try
            {
                await _driveService.AuthenticateAsync("credentials.json");
                SetGoogleProfileUi(true);
                TxtLog.Text = $"Login berhasil sebagai {_driveService.CurrentUser?.Name} ({_driveService.CurrentUser?.Email}).";
            }
            catch (Exception ex)
            {
                SetGoogleProfileUi(false);
                MessageBox.Show($"Gagal login Google:\n{ex.Message}", "Login Google", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ChkAutoLoginDrive_Changed(object sender, RoutedEventArgs e)
        {
            SavePreferences();
        }

        // ==================== CAMERA DISCOVERY & SCAN ====================

        private async Task RefreshCamerasAndScanAsync()
        {
            BtnRefreshCameras.IsEnabled = false;
            TxtLog.Text = "Mencari kamera USB dan Wi-Fi di jaringan...";

            foreach (var src in _availableSources) src.Disconnect();
            _availableSources.Clear();
            CmbCameras.Items.Clear();

            var devices = MediaDevice.GetDevices();
            foreach (var dev in devices)
            {
                _availableSources.Add(new UsbCameraSource(dev));
            }

            var wifiIps = await _wifiDiscovery.DiscoverCameraIpsAsync(1200);
            foreach (var ip in wifiIps)
            {
                _availableSources.Add(new WifiPtpIpCameraSource(ip));
            }

            foreach (var src in _availableSources)
            {
                CmbCameras.Items.Add(src);
            }

            if (CmbCameras.Items.Count > 0)
            {
                CmbCameras.SelectedIndex = 0;
                TxtCameraHint.Text = $"{CmbCameras.Items.Count} sumber kamera terdeteksi dan siap dibaca.";
            }
            else
            {
                _mediaItems.Clear();
                UpdateSummaryStats();
                TxtCameraHint.Text = "Belum ada kamera terdeteksi. Colok USB atau masukkan IP Wi-Fi kamera.";
                TxtLog.Text = "Tidak ada kamera yang terdeteksi.";
            }

            BtnRefreshCameras.IsEnabled = true;
        }

        private async void CmbCameras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCameras.SelectedItem is not ICameraSource selectedCamera) return;

            TxtLog.Text = $"Membaca daftar media dari {selectedCamera.DisplayName}...";
            _mediaItems.Clear();
            UpdateSummaryStats();

            try
            {
                var items = await selectedCamera.ScanMediaAsync();
                foreach (var item in items)
                {
                    item.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(CameraMediaItem.IsSelected))
                            UpdateSummaryStats();
                    };
                    _mediaItems.Add(item);
                }

                ApplySortOrder();
                _mediaView?.Refresh();
                UpdateSummaryStats();
                TxtLog.Text = $"Ditemukan {items.Count} file media pada {selectedCamera.DisplayName}. Klik ganda foto untuk mengedit.";
            }
            catch (Exception ex)
            {
                TxtLog.Text = "Gagal membaca media dari kamera.";
                MessageBox.Show($"Gagal membaca media dari kamera:\n{ex.Message}", "Koneksi Kamera", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCamerasAndScanAsync();
        }

        private void BtnAddWifiIp_Click(object sender, RoutedEventArgs e)
        {
            string ip = TxtWifiIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Masukkan alamat IP kamera terlebih dahulu (contoh: 192.168.1.45).", "IP Kosong", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var wifiCam = new WifiPtpIpCameraSource(ip);
            _availableSources.Add(wifiCam);
            CmbCameras.Items.Add(wifiCam);
            CmbCameras.SelectedItem = wifiCam;
        }

        // ==================== PHOTO EDITOR INTEGRATION ====================

        private async Task OpenPhotoEditorForItemAsync(CameraMediaItem item)
        {
            if (!item.IsEditableImage)
            {
                MessageBox.Show("Editor foto saat ini mendukung format JPG, JPEG, dan PNG.", "Format Tidak Didukung", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CmbCameras.SelectedItem is not ICameraSource activeCamera) return;

            try
            {
                if (string.IsNullOrWhiteSpace(item.LocalCachedPath) || !File.Exists(item.LocalCachedPath))
                {
                    TxtLog.Text = $"Mengunduh pratinjau {item.FileName} dari kamera untuk diedit...";
                    string previewCacheDir = Path.Combine(Path.GetTempPath(), "LensDrop_EditCache");
                    item.LocalCachedPath = await activeCamera.DownloadFileAsync(item, previewCacheDir);
                }

                var editorWin = new PhotoEditorWindow(item.FileName, item.LocalCachedPath, item.EditSettings)
                {
                    Owner = this
                };

                if (editorWin.ShowDialog() == true)
                {
                    item.EditSettings = editorWin.ResultSettings;
                    item.IsSelected = true;
                    _mediaView?.Refresh();
                    TxtLog.Text = item.HasPendingEdits
                        ? $"Edit diterapkan pada {item.FileName} (Preset: {item.EditSettings.PresetName})."
                        : $"Foto {item.FileName} dikembalikan ke pengaturan asli.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal membuka editor foto:\n{ex.Message}", "Studio Edit Foto", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnEditSelectedPhoto_Click(object sender, RoutedEventArgs e)
        {
            var item = GridMedia.SelectedItem as CameraMediaItem
                       ?? _mediaItems.FirstOrDefault(x => x.IsSelected && x.IsEditableImage);

            if (item == null)
            {
                MessageBox.Show("Pilih satu baris foto (JPG/PNG) di tabel terlebih dahulu.", "Pilih Foto", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await OpenPhotoEditorForItemAsync(item);
        }

        private async void BtnEditRowPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CameraMediaItem item)
            {
                await OpenPhotoEditorForItemAsync(item);
            }
        }

        private async void GridMedia_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridMedia.SelectedItem is CameraMediaItem item && item.IsEditableImage)
            {
                await OpenPhotoEditorForItemAsync(item);
            }
        }

        // ==================== FILTER, SORTIR & SELECTION ====================

        private bool FilterMediaPredicate(object obj)
        {
            if (obj is not CameraMediaItem item) return false;

            string query = TxtSearchMedia.Text.Trim();
            if (!string.IsNullOrEmpty(query) && !item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;

            return CmbFilterType.SelectedIndex switch
            {
                1 => item.MediaType == "Image",
                2 => item.MediaType == "Video",
                3 => item.HasPendingEdits,
                _ => true
            };
        }

        private void ApplySortOrder()
        {
            if (_mediaView == null) return;
            _mediaView.SortDescriptions.Clear();

            switch (CmbSortOrder.SelectedIndex)
            {
                case 0: // Nama A-Z
                    _mediaView.SortDescriptions.Add(new SortDescription(nameof(CameraMediaItem.FileName), ListSortDirection.Ascending));
                    break;
                case 1: // Nama Z-A
                    _mediaView.SortDescriptions.Add(new SortDescription(nameof(CameraMediaItem.FileName), ListSortDirection.Descending));
                    break;
                case 2: // Ukuran Terbesar
                    _mediaView.SortDescriptions.Add(new SortDescription(nameof(CameraMediaItem.FileSizeBytes), ListSortDirection.Descending));
                    break;
                case 3: // Ukuran Terkecil
                    _mediaView.SortDescriptions.Add(new SortDescription(nameof(CameraMediaItem.FileSizeBytes), ListSortDirection.Ascending));
                    break;
                case 4: // Tipe File
                    _mediaView.SortDescriptions.Add(new SortDescription(nameof(CameraMediaItem.Extension), ListSortDirection.Ascending));
                    break;
            }
        }

        private void CmbFilterType_SelectionChanged(object sender, SelectionChangedEventArgs e) => _mediaView?.Refresh();
        private void CmbSortOrder_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySortOrder();
        private void TxtSearchMedia_TextChanged(object sender, TextChangedEventArgs e) => _mediaView?.Refresh();

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _mediaItems) item.IsSelected = true;
            UpdateSummaryStats();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _mediaItems) item.IsSelected = false;
            UpdateSummaryStats();
        }

        private void UpdateSummaryStats()
        {
            var selected = _mediaItems.Where(x => x.IsSelected).ToList();
            long totalBytes = selected.Sum(x => x.FileSizeBytes);
            double totalMb = totalBytes / (1024.0 * 1024.0);
            int editedCount = selected.Count(x => x.HasPendingEdits);

            string editSuffix = editedCount > 0 ? $" • {editedCount} diedit" : "";
            TxtSummaryStats.Text = $"{selected.Count} dari {_mediaItems.Count} file dipilih ({totalMb:F1} MB){editSuffix}";
        }

        private void UpdateFolderPreview()
        {
            if (!_isUiReady) return;
            string preview = _sorterService.BuildPreviewPath(
                TxtOutputDir.Text,
                ChkSeparateType.IsChecked == true,
                ChkGroupDate.IsChecked == true,
                ChkGroupModel.IsChecked == true,
                TxtCustomEventFolder.Text
            );
            TxtFolderPreview.Text = $"Target: {preview}";
            SavePreferences();
        }

        private void SortRule_Changed(object sender, RoutedEventArgs e) => UpdateFolderPreview();
        private void TxtCustomEventFolder_TextChanged(object sender, TextChangedEventArgs e) => UpdateFolderPreview();

        private void BtnBrowseDir_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Pilih Folder Utama Penyimpanan Media",
                UseDescriptionForTitle = true,
                SelectedPath = TxtOutputDir.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtOutputDir.Text = dialog.SelectedPath;
                UpdateFolderPreview();
            }
        }

        private void BtnOpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = TxtOutputDir.Text;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }

        // ==================== EXPORT, EDIT & UPLOAD PIPELINE ====================

        private async void BtnStartProcess_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCameras.SelectedItem is not ICameraSource activeCamera)
            {
                MessageBox.Show("Pilih sumber kamera terlebih dahulu.", "Kamera Belum Dipilih", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItems = _mediaItems.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Pilih minimal satu file foto/video untuk diproses.", "Tidak Ada File", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool uploadToDrive = ChkUploadDrive.IsChecked == true;
            if (uploadToDrive && !_driveService.IsAuthenticated)
            {
                var ask = MessageBox.Show(
                    "Akun Google Drive belum login. Ingin login sekarang sebelum memulai transfer?",
                    "Login Google Drive",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (ask == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _driveService.AuthenticateAsync("credentials.json");
                        SetGoogleProfileUi(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Login Google gagal:\n{ex.Message}", "Error Auth", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    uploadToDrive = false;
                }
            }

            BtnStartProcess.IsEnabled = false;
            BtnRefreshCameras.IsEnabled = false;
            string stagingDir = Path.Combine(Path.GetTempPath(), "LensDrop_Staging");
            Directory.CreateDirectory(stagingDir);

            bool separateType = ChkSeparateType.IsChecked == true;
            bool groupDate = ChkGroupDate.IsChecked == true;
            bool groupModel = ChkGroupModel.IsChecked == true;
            bool renamePrefix = ChkRenameTimestamp.IsChecked == true;
            string customEventFolder = TxtCustomEventFolder.Text.Trim();
            string baseOutputDir = TxtOutputDir.Text;
            string driveRootFolder = string.IsNullOrWhiteSpace(TxtDriveRootFolder.Text) ? "LensDrop_Backup" : TxtDriveRootFolder.Text.Trim();

            int total = selectedItems.Count;
            int completed = 0;

            foreach (var item in selectedItems)
            {
                try
                {
                    item.StatusCategory = "Processing";
                    item.Status = "Mengunduh dari kamera...";
                    TxtLog.Text = $"[{completed + 1}/{total}] Mengunduh {item.FileName}...";

                    string workingFile;
                    if (!string.IsNullOrWhiteSpace(item.LocalCachedPath) && File.Exists(item.LocalCachedPath))
                    {
                        workingFile = Path.Combine(stagingDir, item.FileName);
                        File.Copy(item.LocalCachedPath, workingFile, overwrite: true);
                    }
                    else
                    {
                        workingFile = await activeCamera.DownloadFileAsync(item, stagingDir);
                    }

                    // Terapkan edit foto jika ada
                    if (item.HasPendingEdits && item.EditSettings != null)
                    {
                        item.Status = "Menerapkan edit foto...";
                        TxtLog.Text = $"[{completed + 1}/{total}] Merender edit foto {item.FileName}...";
                        string editedTemp = Path.Combine(stagingDir, $"edited_{item.FileName}");
                        await Task.Run(() => _photoEditorService.ApplyEditsToFile(workingFile, editedTemp, item.EditSettings));
                        File.Copy(editedTemp, workingFile, overwrite: true);
                        File.Delete(editedTemp);
                    }

                    item.Status = "Menyortir folder lokal...";
                    var sortResult = _sorterService.SortFile(
                        workingFile,
                        baseOutputDir,
                        item.MediaType,
                        separateType,
                        groupDate,
                        groupModel,
                        customEventFolder,
                        renamePrefix
                    );

                    if (uploadToDrive && _driveService.IsAuthenticated)
                    {
                        item.Status = "Mengunggah ke Drive...";
                        string driveFolderId = await _driveService.EnsureFolderHierarchyAsync(driveRootFolder, sortResult.RelativeFolderHierarchy);
                        await _driveService.UploadFileAsync(sortResult.LocalSortedPath, driveFolderId, pct =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                item.Status = $"Upload Drive ({pct}%)";
                                TxtLog.Text = $"[{completed + 1}/{total}] Mengunggah {item.FileName} ({pct}%)...";
                            });
                        });
                        item.Status = "Tersortir & Tersinkron ✓";
                    }
                    else
                    {
                        item.Status = "Tersortir di Lokal ✓";
                    }

                    item.StatusCategory = "Done";
                }
                catch (Exception ex)
                {
                    item.StatusCategory = "Error";
                    item.Status = $"Gagal: {ex.Message}";
                }

                completed++;
                double overallPct = (completed * 100.0) / total;
                ProgressTotal.Value = overallPct;
                TxtProgressPercent.Text = $"{overallPct:F0}%";
            }

            TxtLog.Text = $"Selesai! {completed} dari {total} file berhasil diproses ke {baseOutputDir}.";
            BtnStartProcess.IsEnabled = true;
            BtnRefreshCameras.IsEnabled = true;
        }

        private async void BtnBrowseDriveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!_driveService.IsAuthenticated)
            {
                try
                {
                    await _driveService.AuthenticateAsync("credentials.json");
                    SetGoogleProfileUi(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Silakan login Google terlebih dahulu:\n{ex.Message}", "Login Google", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var picker = new DriveFolderPickerWindow(_driveService) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                TxtDriveRootFolder.Text = picker.SelectedFolderPath;
                SavePreferences();
                TxtLog.Text = $"Folder tujuan Google Drive diatur ke: {(string.IsNullOrWhiteSpace(picker.SelectedFolderPath) ? "My Drive (Root)" : picker.SelectedFolderPath)}";
            }
        }

        private void TxtDriveRootFolder_TextChanged(object sender, TextChangedEventArgs e) => SavePreferences();
    }
}
