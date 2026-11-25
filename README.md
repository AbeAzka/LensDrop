# 📸 LensDrop Studio

**LensDrop Studio** adalah aplikasi desktop Windows berbasis **.NET 8 (WPF)** yang dirancang untuk mempermudah fotografer dan kreator konten dalam mentransfer, menyortir, mengedit, serta mencadangkan (*auto-sync*) foto dan video secara langsung dari kamera DSLR/Mirrorless (seperti Canon EOS) ke komputer lokal dan **Google Drive**.

---

## ✨ Fitur Utama

* **🔌 Koneksi Kamera Ganda (USB MTP & Wi-Fi PTP/IP):**
  * Deteksi otomatis perangkat kamera melalui kabel USB (*Plug & Play* dengan pemantauan `WM_DEVICECHANGE`).
  * Dukungan koneksi nirkabel **PTP/IP** melalui jaringan Wi-Fi lokal dengan *Client GUID* statis agar proses *pairing* kamera Canon berjalan stabil.
* **🎨 Studio Edit Foto Non-Destruktif:**
  * Pratinjau *real-time* untuk file `.JPG`, `.JPEG`, dan `.PNG` sebelum diekspor.
  * **Preset Cepat:** *Original*, *Vivid*, *B&W Classic*, *Cinematic*, dan *Bright Pop*.
  * **Penyesuaian Manual & Transformasi:** Pengaturan *Brightness*, *Contrast*, *Saturation*, rotasi $90^\circ$, *Flip Horizontal*, serta kontrol kualitas kompresi JPEG ($60\% - 100\%$).
* **🗂️ Sortir Otomatis Berbasis Metadata EXIF:**
  * Pemisahan folder otomatis antara `Images` dan `Videos`.
  * Pengelompokan folder dinamis berdasarkan tanggal pengambilan gambar (`YYYY-MM-DD`), model kamera dari tag EXIF, serta sub-folder *Event/Project* kustom.
  * Opsi penambahan *prefix* stempel waktu (`yyyyMMdd_HHmmss_`) pada nama file hasil ekspor.
* **☁️ Integrasi & Auto-Sync Google Drive:**
  * Login akun Google menggunakan **OAuth 2.0** dengan tampilan profil pengguna (Nama, Email, dan Avatar).
  * Fitur **Auto-Login** sesi Google Drive saat aplikasi dijalankan.
  * Pemilihan dan pembuatan folder tujuan langsung di Google Drive dari dalam aplikasi (*Drive Folder Picker*) dengan struktur sub-folder yang mencerminkan direktori lokal.
* **📦 Siap Distribusi (Windows Installer):**
  * Dilengkapi skrip otomatis (`Build_Installer.bat` & `LensDrop_Setup.iss`) untuk memaketkan aplikasi menjadi installer `.exe` mandiri (*Self-Contained*).

---

## 🛠️ Teknologi yang Digunakan

* **Framework:** .NET 8.0 (`net8.0-windows`) & Windows Presentation Foundation (WPF)
* **Protokol Kamera:** `MediaDevices` (WPD/MTP untuk USB) & Custom TCP Socket PTP/IP (`Port 15740`)
* **Metadata & Pengolahan Gambar:** `MetadataExtractor` & `System.Drawing` (`ColorMatrix`)
* **Cloud & Autentikasi:** `Google.Apis.Drive.v3` & `Google.Apis.Oauth2.v2`
* **Installer:** Inno Setup 6

---

## 🚀 Panduan Instalasi & Konfigurasi Pengembangan

### 1. Prasyarat
* **Windows 10 / 11** (64-bit)
* **.NET 8.0 SDK** & **Visual Studio 2022** (dengan workload *.NET desktop development*)
* **Inno Setup 6** *(opsional, hanya diperlukan jika ingin membangun file installer `.exe`)*

### 2. Konfigurasi Google OAuth (`credentials.json`)
Demi keamanan, kredensial OAuth tidak disertakan di dalam repositori ini. Untuk mengaktifkan fitur Google Drive:
1. Buka [Google Cloud Console](https://console.cloud.google.com/).
2. Buat proyek baru dan aktifkan **Google Drive API**.
3. Masuk ke **APIs & Services > Credentials**, buat **OAuth 2.0 Client ID** dengan tipe aplikasi **Desktop app**.
4. Unduh file JSON kredensial tersebut, ubah namanya menjadi `credentials.json`, dan letakkan di folder root proyek (sejajar dengan `LensDrop.csproj`).
   *(Anda dapat melihat struktur formatnya pada file `credentials.example.json`.)*

### 3. Menjalankan Aplikasi
1. Clone repositori ini:
   ```bash
   git clone https://github.com/USERNAME_KAMU/LensDrop.git
   cd LensDrop
   ```
2. Buka `LensDrop.csproj` menggunakan **Visual Studio 2022**.
3. Pastikan file `credentials.json` sudah berada di folder root proyek.
4. Tekan **`F5`** untuk melakukan *Build* dan menjalankan aplikasi.

---

## 📦 Membuat File Installer Windows (`.exe`)

Proyek ini sudah menyediakan skrip otomatisasi di dalam folder `Installer/`:
1. Pastikan **Inno Setup 6** sudah terinstal di komputer Anda.
2. Klik ganda file **`Installer\Build_Installer.bat`**.
3. Skrip akan otomatis menjalankan `dotnet publish` (*Self-Contained Single-File* untuk `win-x64`) dan mengompilasi paket setup.
4. File installer akhir akan tersimpan di folder **`InstallerOutput\LensDrop_Studio_Setup_v2.0.0.exe`**.

---

## 📷 Tips Koneksi Kamera Canon

* **Via Kabel USB:** Pastikan fitur **Wi-Fi / NFC** di menu kamera Canon dalam keadaan **Nonaktif (Disable)** dan tutup aplikasi **Canon EOS Utility** di *system tray* Windows agar port MTP tidak terkunci.
* **Via Wi-Fi (PTP/IP):** Masuk ke menu **Fungsi Wi-Fi** pada kamera, pilih mode **Kendali jarak jauh (EOS Utility)** (*Remote control*), hubungkan kamera dan PC ke jaringan Wi-Fi yang sama, lalu masukkan alamat IP kamera ke dalam **LensDrop Studio**.

---

## 📄 Lisensi

Dikembangkan oleh **Indodev Studio**. Hak cipta dilindungi undang-undang.
