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
        private const string TokenStoreFolder = "LensDropDriveTokens";
        private const string TokenUserKey = "user";

        private static readonly string[] Scopes =
        {
            DriveService.Scope.DriveFile,
            Oauth2Service.Scope.UserinfoProfile,
            Oauth2Service.Scope.UserinfoEmail
        };

        public bool IsAuthenticated => _driveService != null;
        public GoogleUserProfile? CurrentUser { get; private set; }

        public string GetCredentialsPath(string fileName = "credentials.json")
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        public bool HasCredentialsFile(string fileName = "credentials.json")
        {
            return File.Exists(GetCredentialsPath(fileName));
        }

        public bool HasSavedToken()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string tokenFilePath = Path.Combine(
                appDataPath,
                TokenStoreFolder,
                $"Google.Apis.Auth.OAuth2.Responses.TokenResponse-{TokenUserKey}"
            );
            return File.Exists(tokenFilePath);
        }

        public async Task<bool> TrySilentAuthenticateAsync(string fileName = "credentials.json")
        {
            if (!HasSavedToken() || !HasCredentialsFile(fileName))
                return false;

            try
            {
                return await AuthenticateAsync(fileName);
            }
            catch
            {
                _driveService = null;
                CurrentUser = null;
                return false;
            }
        }

        public async Task<bool> AuthenticateAsync(string fileName = "credentials.json")
        {
            string fullPath = GetCredentialsPath(fileName);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"File '{fileName}' tidak ditemukan di folder aplikasi:\n{fullPath}\n\nPastikan file OAuth Client ID (Desktop App) sudah ditaruh di folder aplikasi."
                );

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                TokenUserKey,
                CancellationToken.None,
                new FileDataStore(TokenStoreFolder, true)
            );

            var initializer = new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "LensDrop Studio by Indodev Studio"
            };

            _driveService = new DriveService(initializer);

            // Ambil informasi profil Google (Nama, Email, Foto Profil)
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
                // Fallback jika scope userinfo belum disetujui pada token lama
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
            var store = new FileDataStore(TokenStoreFolder, true);
            await store.ClearAsync();
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
