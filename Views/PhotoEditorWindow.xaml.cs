using LensDrop.Models;
using LensDrop.Services;
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LensDrop.Views
{
    public partial class PhotoEditorWindow : Window
    {
        private readonly string _sourceImagePath;
        private readonly PhotoEditorService _editorService = new();
        private readonly PhotoEditSettings _workingSettings;
        private bool _isInitializing = true;

        public PhotoEditSettings ResultSettings => _workingSettings;

        public PhotoEditorWindow(string fileName, string sourceImagePath, PhotoEditSettings? existingSettings)
        {
            InitializeComponent();
            _sourceImagePath = sourceImagePath;
            TxtFileName.Text = fileName;

            _workingSettings = existingSettings != null
                ? new PhotoEditSettings
                {
                    RotationDegrees = existingSettings.RotationDegrees,
                    FlipHorizontal = existingSettings.FlipHorizontal,
                    Brightness = existingSettings.Brightness,
                    Contrast = existingSettings.Contrast,
                    Saturation = existingSettings.Saturation,
                    JpegQuality = existingSettings.JpegQuality,
                    PresetName = existingSettings.PresetName
                }
                : new PhotoEditSettings();

            SyncSlidersFromSettings();
            _isInitializing = false;
            RefreshPreview();
        }

        private void SyncSlidersFromSettings()
        {
            SliderBrightness.Value = _workingSettings.Brightness;
            SliderContrast.Value = _workingSettings.Contrast;
            SliderSaturation.Value = _workingSettings.Saturation;
            SliderQuality.Value = _workingSettings.JpegQuality;
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            TxtBrightnessVal.Text = $"{(_workingSettings.Brightness >= 0 ? "+" : "")}{Math.Round(_workingSettings.Brightness * 100)}%";
            TxtContrastVal.Text = $"{Math.Round(_workingSettings.Contrast * 100)}%";
            TxtSaturationVal.Text = $"{Math.Round(_workingSettings.Saturation * 100)}%";
            TxtQualityVal.Text = $"{_workingSettings.JpegQuality}%";
            TxtTransformInfo.Text = $"Rotasi: {_workingSettings.RotationDegrees}° | Flip: {(_workingSettings.FlipHorizontal ? "Ya" : "Tidak")} | Preset: {_workingSettings.PresetName}";
        }

        private void RefreshPreview()
        {
            if (_isInitializing) return;
            try
            {
                using var bmp = _editorService.RenderPreviewBitmap(_sourceImagePath, _workingSettings, 1100);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();

                ImgPreview.Source = bi;
                UpdateLabels();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat pratinjau gambar:{ex.Message}", "Editor Foto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            _workingSettings.Brightness = SliderBrightness.Value;
            _workingSettings.Contrast = SliderContrast.Value;
            _workingSettings.Saturation = SliderSaturation.Value;
            _workingSettings.JpegQuality = (int)SliderQuality.Value;
            _workingSettings.PresetName = "Custom";
            RefreshPreview();
        }

        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag) return;

            _isInitializing = true;
            switch (tag)
            {
                case "Original":
                    _workingSettings.Brightness = 0.0;
                    _workingSettings.Contrast = 1.0;
                    _workingSettings.Saturation = 1.0;
                    break;
                case "Vivid":
                    _workingSettings.Brightness = 0.04;
                    _workingSettings.Contrast = 1.15;
                    _workingSettings.Saturation = 1.35;
                    break;
                case "BW":
                    _workingSettings.Brightness = 0.02;
                    _workingSettings.Contrast = 1.22;
                    _workingSettings.Saturation = 0.0;
                    break;
                case "Cinematic":
                    _workingSettings.Brightness = -0.04;
                    _workingSettings.Contrast = 1.18;
                    _workingSettings.Saturation = 0.85;
                    break;
                case "Bright":
                    _workingSettings.Brightness = 0.14;
                    _workingSettings.Contrast = 1.06;
                    _workingSettings.Saturation = 1.12;
                    break;
            }
            _workingSettings.PresetName = tag;
            SyncSlidersFromSettings();
            _isInitializing = false;
            RefreshPreview();
        }

        private void BtnRotateLeft_Click(object sender, RoutedEventArgs e)
        {
            _workingSettings.RotationDegrees = (_workingSettings.RotationDegrees + 270) % 360;
            RefreshPreview();
        }

        private void BtnRotateRight_Click(object sender, RoutedEventArgs e)
        {
            _workingSettings.RotationDegrees = (_workingSettings.RotationDegrees + 90) % 360;
            RefreshPreview();
        }

        private void BtnFlipH_Click(object sender, RoutedEventArgs e)
        {
            _workingSettings.FlipHorizontal = !_workingSettings.FlipHorizontal;
            RefreshPreview();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            _workingSettings.RotationDegrees = 0;
            _workingSettings.FlipHorizontal = false;
            _workingSettings.Brightness = 0.0;
            _workingSettings.Contrast = 1.0;
            _workingSettings.Saturation = 1.0;
            _workingSettings.JpegQuality = 95;
            _workingSettings.PresetName = "Original";
            SyncSlidersFromSettings();
            _isInitializing = false;
            RefreshPreview();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
