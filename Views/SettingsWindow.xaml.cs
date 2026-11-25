using LensDrop.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LensDrop.Views
{
    public partial class SettingsWindow : Window
    {
        public AppUserPreferences WorkingPrefs { get; private set; }
        public bool ResetRequested { get; private set; } = false;

        public SettingsWindow(AppUserPreferences currentPrefs)
        {
            InitializeComponent();
            WorkingPrefs = new AppUserPreferences
            {
                AutoLoginGoogle = currentPrefs.AutoLoginGoogle,
                OutputDirectory = currentPrefs.OutputDirectory,
                DriveRootFolder = currentPrefs.DriveRootFolder,
                SeparateType = currentPrefs.SeparateType,
                GroupDate = currentPrefs.GroupDate,
                GroupModel = currentPrefs.GroupModel,
                RenameTimestamp = currentPrefs.RenameTimestamp,
                DefaultUploadToDrive = currentPrefs.DefaultUploadToDrive,
                IncludeSubfoldersOnFolderImport = currentPrefs.IncludeSubfoldersOnFolderImport,
                CopyLocalFolderFiles = currentPrefs.CopyLocalFolderFiles,
                AutoScanUsbOnConnect = currentPrefs.AutoScanUsbOnConnect,
                DefaultJpegQuality = currentPrefs.DefaultJpegQuality
            };

            // Set pilihan dropdown sesuai tema yang sedang aktif
            CmbThemeSetting.SelectedIndex = ThemeManager.CurrentTheme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };

            ChkSettingAutoLogin.IsChecked = WorkingPrefs.AutoLoginGoogle;
            ChkSettingAutoUpload.IsChecked = WorkingPrefs.DefaultUploadToDrive;
            ChkSettingIncludeSubfolders.IsChecked = WorkingPrefs.IncludeSubfoldersOnFolderImport;
            ChkSettingCopyInsteadMove.IsChecked = WorkingPrefs.CopyLocalFolderFiles;
            ChkSettingAutoScanUsb.IsChecked = WorkingPrefs.AutoScanUsbOnConnect;
            SliderDefaultJpegQuality.Value = Math.Clamp(WorkingPrefs.DefaultJpegQuality, 70, 100);
            TxtDefaultQualityVal.Text = $"{(int)SliderDefaultJpegQuality.Value}%";
            TxtTokenLocation.Text = $"Lokasi Token Sesi: {GoogleDriveSyncService.TokenStoreFullPath}";
        }

        private void CmbThemeSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            if (CmbThemeSetting.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            {
                ThemeManager.ApplyTheme(mode);
            }
        }

        private void SliderDefaultJpegQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDefaultQualityVal != null)
                TxtDefaultQualityVal.Text = $"{(int)SliderDefaultJpegQuality.Value}%";
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] tempDirs =
                {
                    Path.Combine(Path.GetTempPath(), "LensDrop_EditCache"),
                    Path.Combine(Path.GetTempPath(), "LensDrop_Staging")
                };

                foreach (var dir in tempDirs)
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }

                MessageBox.Show("Cache pratinjau & staging sementara berhasil dibersihkan.", "Bersihkan Cache", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sebagian file cache sedang digunakan: {ex.Message}", "Cache", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnResetAllPrefs_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Kembalikan semua pengaturan ke bawaan pabrik?", "Reset Pengaturan", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                ResetRequested = true;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            WorkingPrefs.AutoLoginGoogle = ChkSettingAutoLogin.IsChecked == true;
            WorkingPrefs.DefaultUploadToDrive = ChkSettingAutoUpload.IsChecked == true;
            WorkingPrefs.IncludeSubfoldersOnFolderImport = ChkSettingIncludeSubfolders.IsChecked == true;
            WorkingPrefs.CopyLocalFolderFiles = ChkSettingCopyInsteadMove.IsChecked == true;
            WorkingPrefs.AutoScanUsbOnConnect = ChkSettingAutoScanUsb.IsChecked == true;
            WorkingPrefs.DefaultJpegQuality = (int)SliderDefaultJpegQuality.Value;

            DialogResult = true;
            Close();
        }
    }
}
