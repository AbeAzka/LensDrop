using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Oauth2.v2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using LensDrop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public class DriveFolderItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
    }

    public class GoogleDriveSyncService
    {
        private DriveService? _driveService;
        private readonly Dictionary<string, string> _folderIdCache = new();
        private const string TokenUserKey = "lensdrop_default_user";

        private static readonly string[] Scopes =
        {
            DriveService.Scope.DriveFile,
            Oauth2Service.Scope.UserinfoProfile,
            Oauth2Service.Scope.UserinfoEmail
        };

        // Lokasi folder penyimpanan token yang konsisten di AppData\Roaming\LensDrop\DriveTokens
        public static string TokenStoreFullPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LensDrop",
            "DriveTokens"
        );

        public bool IsAuthenticated => _driveService != null;
        public GoogleUserProfile? CurrentUser { get; private set; }
        public string? LastAuthError { get; private set; }

        public string GetCredentialsPath(string fileName = "credentials.json")
        {
            // 1. Cek di folder instalasi / executable (AppContext.BaseDirectory aman untuk PublishSingleFile)
            string baseDir = AppContext.BaseDirectory;
            string p1 = Path.Combine(baseDir, fileName);
            if (File.Exists(p1)) return p1;

            // 2. Cek di AppData\Roaming\LensDrop\credentials.json (jika user meletakkan manual di AppData)
            string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LensDrop", fileName);
            if (File.Exists(p2)) return p2;

            // 3. Cek di working directory saat ini
            string p3 = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            if (File.Exists(p3)) return p3;

            return p1;
        }

        public bool HasCredentialsFile(string fileName = "credentials.json")
        {
            return File.Exists(GetCredentialsPath(fileName));
        }

        public bool HasSavedToken()
        {
            try
            {
                if (!Directory.Exists(TokenStoreFullPath)) return false;
                // Cek apakah ada file TokenResponse apapun di folder TokenStoreFullPath (termasuk migrasi dari folder lama)
                var files = Directory.GetFiles(TokenStoreFullPath, "*TokenResponse*");
                if (files.Length > 0) return true;

                // Migrasi otomatis dari folder lama (%AppData%\LensDropDriveTokens) jika ada
                string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LensDropDriveTokens");
                if (Directory.Exists(legacyPath))
                {
                    var legacyFiles = Directory.GetFiles(legacyPath, "*TokenResponse*");
                    if (legacyFiles.Length > 0)
                    {
                        Directory.CreateDirectory(TokenStoreFullPath);
                        foreach (var lf in legacyFiles)
                        {
                            string dest = Path.Combine(TokenStoreFullPath, $"Google.Apis.Auth.OAuth2.Responses.TokenResponse-{TokenUserKey}");
                            File.Copy(lf, dest, overwrite: true);
                        }
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> TrySilentAuthenticateAsync(string fileName = "credentials.json")
        {
            LastAuthError = null;
            if (!HasSavedToken())
            {
                LastAuthError = "Belum ada sesi login tersimpan.";
                return false;
            }

            if (!HasCredentialsFile(fileName))
            {
                LastAuthError = "File credentials.json tidak ditemukan.";
                return false;
            }

            try
            {
                return await AuthenticateCoreAsync(fileName, allowInteractiveBrowser: false);
            }
            catch (Exception ex)
            {
                LastAuthError = ex.Message;
                _driveService = null;
                CurrentUser = null;
                return false;
            }
        }

        public async Task<bool> AuthenticateAsync(string fileName = "credentials.json")
        {
            LastAuthError = null;
            return await AuthenticateCoreAsync(fileName, allowInteractiveBrowser: true);
        }

        private async Task<bool> AuthenticateCoreAsync(string fileName, bool allowInteractiveBrowser)
        {
            string fullPath = GetCredentialsPath(fileName);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"File '{fileName}' tidak ditemukan di folder aplikasi:\n{fullPath}\n\nPastikan file OAuth Client ID (Desktop App) sudah ditaruh di folder aplikasi atau %AppData%\\LensDrop\\."
                );

            Directory.CreateDirectory(TokenStoreFullPath);
            var dataStore = new FileDataStore(TokenStoreFullPath, fullPath: true);

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            var clientSecrets = GoogleClientSecrets.FromStream(stream).Secrets;

            // Jika silent auto-login, batasi waktu tunggu agar tidak menggantung jika token dicabut
            using var cts = allowInteractiveBrowser
                ? new CancellationTokenSource(TimeSpan.FromMinutes(3))
                : new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                Scopes,
                TokenUserKey,
                cts.Token,
                dataStore
            );

            // Paksa refresh access token jika sudah kedaluwarsa menggunakan RefreshToken tersimpan
            if (credential.Token.IsStale && !string.IsNullOrEmpty(credential.Token.RefreshToken))
            {
                bool refreshed = await credential.RefreshTokenAsync(cts.Token);
                if (!refreshed && !allowInteractiveBrowser)
                {
                    throw new InvalidOperationException("Refresh token kedaluwarsa, silakan login ulang secara manual.");
                }
            }

            var initializer = new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "LensDrop Studio v0.2 by Indodev Studio"
            };

            _driveService = new DriveService(initializer);

            try
            {
                var oauthService = new Oauth2Service(initializer);
                var userInfo = await oauthService.Userinfo.Get().ExecuteAsync();
                CurrentUser = new GoogleUserProfile
                {
                    Name = userInfo.Name ?? userInfo.Email ?? "Pengguna Google",
                    Email = userInfo.Email ?? string.Empty,
                    PictureUrl = userInfo.Picture ?? string.Empty
                };
            }
            catch
            {
                var aboutReq = _driveService.About.Get();
                aboutReq.Fields = "user(displayName,emailAddress,photoLink)";
                var about = await aboutReq.ExecuteAsync();
                CurrentUser = new GoogleUserProfile
                {
                    Name = about.User?.DisplayName ?? "Pengguna Google Drive",
                    Email = about.User?.EmailAddress ?? string.Empty,
                    PictureUrl = about.User?.PhotoLink ?? string.Empty
                };
            }

            return true;
        }

        public async Task LogoutAsync()
        {
            if (Directory.Exists(TokenStoreFullPath))
            {
                var store = new FileDataStore(TokenStoreFullPath, fullPath: true);
                await store.ClearAsync();
            }
            _driveService = null;
            CurrentUser = null;
            _folderIdCache.Clear();
        }

        public async Task<string> EnsureFolderHierarchyAsync(string rootFolderName, string relativePath)
        {
            if (_driveService == null) throw new InvalidOperationException("Belum login Google Drive.");

            if (string.IsNullOrWhiteSpace(rootFolderName) && string.IsNullOrWhiteSpace(relativePath))
                return "root";

            string fullPath = string.Join("/", new[] { rootFolderName, relativePath }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (_folderIdCache.TryGetValue(fullPath, out string? cachedId))
                return cachedId;

            string[] parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string parentId = "root";

            foreach (string folderName in parts)
            {
                string query = $"mimeType='application/vnd.google-apps.folder' and name='{folderName.Replace("'", "\\'")}' and '{parentId}' in parents and trashed=false";
                var listRequest = _driveService.Files.List();
                listRequest.Q = query;
                listRequest.Fields = "files(id, name)";

                var existing = await listRequest.ExecuteAsync();
                if (existing.Files != null && existing.Files.Count > 0)
                {
                    parentId = existing.Files.First().Id;
                }
                else
                {
                    var folderMeta = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = folderName,
                        MimeType = "application/vnd.google-apps.folder",
                        Parents = new[] { parentId }
                    };
                    var createReq = _driveService.Files.Create(folderMeta);
                    createReq.Fields = "id";
                    var created = await createReq.ExecuteAsync();
                    parentId = created.Id;
                }
            }

            _folderIdCache[fullPath] = parentId;
            return parentId;
        }

        public async Task<string?> UploadFileAsync(string localFilePath, string driveFolderId, Action<int>? onPercentChanged = null)
        {
            if (_driveService == null) throw new InvalidOperationException("Belum login Google Drive.");

            var fileInfo = new FileInfo(localFilePath);
            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileInfo.Name,
                Parents = new[] { driveFolderId }
            };

            using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
            var request = _driveService.Files.Create(metadata, fs, "application/octet-stream");
            request.ChunkSize = ResumableUpload.MinimumChunkSize * 16;

            request.ProgressChanged += progress =>
            {
                if (progress.Status == UploadStatus.Uploading && fileInfo.Length > 0)
                {
                    int pct = (int)((progress.BytesSent * 100) / fileInfo.Length);
                    onPercentChanged?.Invoke(pct);
                }
            };

            var result = await request.UploadAsync();
            return result.Status == UploadStatus.Completed ? request.ResponseBody?.Id : null;
        }

        public async Task<List<DriveFolderItem>> ListRootFoldersAsync()
        {
            if (_driveService == null) throw new InvalidOperationException("Belum login Google Drive.");

            var request = _driveService.Files.List();
            request.Q = "mimeType='application/vnd.google-apps.folder' and 'root' in parents and trashed=false";
            request.Fields = "files(id, name)";
            request.OrderBy = "name";
            request.PageSize = 100;

            var response = await request.ExecuteAsync();
            var list = new List<DriveFolderItem>
            {
                new DriveFolderItem { Id = "root", Name = "My Drive (Root Utama)", DisplayPath = "" }
            };

            if (response.Files != null)
            {
                foreach (var f in response.Files)
                {
                    list.Add(new DriveFolderItem
                    {
                        Id = f.Id,
                        Name = f.Name,
                        DisplayPath = f.Name
                    });
                }
            }

            return list;
        }

        public async Task<DriveFolderItem> CreateRootFolderAsync(string folderName)
        {
            if (_driveService == null) throw new InvalidOperationException("Belum login Google Drive.");

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName.Trim(),
                MimeType = "application/vnd.google-apps.folder",
                Parents = new[] { "root" }
            };

            var req = _driveService.Files.Create(meta);
            req.Fields = "id, name";
            var created = await req.ExecuteAsync();

            return new DriveFolderItem
            {
                Id = created.Id,
                Name = created.Name,
                DisplayPath = created.Name
            };
        }
    }
}
