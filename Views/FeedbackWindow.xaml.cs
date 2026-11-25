using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LensDrop.Views
{
    public partial class FeedbackWindow : Window
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public FeedbackWindow(string? prefillEmail = null)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(prefillEmail))
                TxtSenderContact.Text = prefillEmail;
        }

        private async void BtnSendFeedback_Click(object sender, RoutedEventArgs e)
        {
            string message = TxtFeedbackMessage.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                MessageBox.Show("Silakan tulis detail pesan feedback terlebih dahulu.", "Pesan Kosong", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string category = (CmbFeedbackCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Umum";
            string contact = TxtSenderContact.Text.Trim();
            string diag = ChkIncludeDiagInfo.IsChecked == true
                ? $"LensDrop Studio v0.2.0 | OS: {Environment.OSVersion}"
                : "Tidak disertakan";

            BtnSendFeedback.IsEnabled = false;
            BtnSendFeedback.Content = "Mengirim...";

            // 1. Simpan salinan lokal di %AppData%\LensDrop\feedback_history.log
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LensDrop");
                Directory.CreateDirectory(logDir);
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] ({contact}) - {message} | {diag}{Environment.NewLine}";
                await File.AppendAllTextAsync(Path.Combine(logDir, "feedback_history.log"), logEntry);
            }
            catch { }

            // 2. Coba kirim ke endpoint Indodev Studio (fallback ke mailto jika offline)
            bool sentOnline = false;
            try
            {
                var payload = new
                {
                    app = "LensDrop Studio",
                    version = "0.2.0",
                    category,
                    contact,
                    message,
                    diagnostics = diag
                };
                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync("https://www.indodevstudio.my.id/api/feedback.php", content);
                sentOnline = resp.IsSuccessStatusCode;
            }
            catch
            {
                sentOnline = false;
            }

            if (sentOnline)
            {
                MessageBox.Show("Terima kasih! Feedback Anda berhasil dikirim ke tim Indodev Studio.", "Feedback Terkirim", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
                return;
            }

            // Fallback: buka email client atau salin ke clipboard agar user tidak kehilangan pesan
            var fallbackChoice = MessageBox.Show(
                "Feedback Anda telah disimpan secara lokal. Ingin langsung mengirimkannya melalui aplikasi Email default ke Indodev Studio?",
                "Kirim via Email",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (fallbackChoice == MessageBoxResult.Yes)
            {
                string subject = Uri.EscapeDataString($"[LensDrop v0.2 Feedback] {category}");
                string body = Uri.EscapeDataString($"Pengirim: {contact} Kategori: {category} Info Sistem: {diag} Pesan: {message}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:hi@indodevstudio.my.id?subject={subject}&body={body}",
                    UseShellExecute = true
                });
            }

            DialogResult = true;
            Close();
        }

        private void BtnOpenWebContact_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.indodevstudio.my.id/contact",
                UseShellExecute = true
            });
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
