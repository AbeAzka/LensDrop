using LensDrop.Services;
using System;
using System.Windows;

namespace LensDrop.Views
{
    public partial class DriveFolderPickerWindow : Window
    {
        private readonly GoogleDriveSyncService _driveService;
        public string SelectedFolderPath { get; private set; } = "LensDrop_Backup";

        public DriveFolderPickerWindow(GoogleDriveSyncService driveService)
        {
            InitializeComponent();
            _driveService = driveService;
            Loaded += async (_, _) => await LoadFoldersAsync();
        }

        private async System.Threading.Tasks.Task LoadFoldersAsync()
        {
            try
            {
                var folders = await _driveService.ListRootFoldersAsync();
                LstFolders.ItemsSource = folders;
                if (folders.Count > 0) LstFolders.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat daftar folder Google Drive:\n{ex.Message}", "Google Drive", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCreateFolder_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            BtnCreateFolder.IsEnabled = false;
            try
            {
                var created = await _driveService.CreateRootFolderAsync(name);
                TxtNewFolder.Clear();
                await LoadFoldersAsync();
                SelectedFolderPath = created.DisplayPath;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal membuat folder di Drive:\n{ex.Message}", "Google Drive", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCreateFolder.IsEnabled = true;
            }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (LstFolders.SelectedItem is DriveFolderItem item)
            {
                SelectedFolderPath = item.DisplayPath;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}