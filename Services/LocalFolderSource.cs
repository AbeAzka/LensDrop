using LensDrop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public class LocalFolderSource : ICameraSource
    {
        private readonly string _folderPath;
        private readonly bool _includeSubfolders;
        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".cr2", ".cr3", ".arw", ".nef", ".raf" };
        private static readonly string[] VideoExts = { ".mp4", ".mov", ".mxf", ".avi" };

        public LocalFolderSource(string folderPath, bool includeSubfolders = true)
        {
            _folderPath = folderPath;
            _includeSubfolders = includeSubfolders;
        }

        public string FolderPath => _folderPath;
        public string DisplayName => $"Folder: {Path.GetFileName(_folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))} ({_folderPath})";
        public string ConnectionType => "Folder";

        public async Task<List<CameraMediaItem>> ScanMediaAsync()
        {
            var items = new List<CameraMediaItem>();
            if (!Directory.Exists(_folderPath)) return items;

            await Task.Run(() =>
            {
                var option = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(_folderPath, "*.*", option);
                }
                catch
                {
                    files = Directory.EnumerateFiles(_folderPath, "*.*", SearchOption.TopDirectoryOnly);
                }

                foreach (var path in files)
                {
                    string fileName = Path.GetFileName(path);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    bool isImg = ImageExts.Contains(ext);
                    bool isVid = VideoExts.Contains(ext);
                    if (!isImg && !isVid) continue;

                    var info = new FileInfo(path);
                    items.Add(new CameraMediaItem
                    {
                        RemotePath = path,
                        LocalCachedPath = path, // Langsung siap diedit/di-upload tanpa unduh ulang
                        FileName = fileName,
                        Extension = ext.ToUpperInvariant(),
                        MediaType = isImg ? "Image" : "Video",
                        FileSizeBytes = info.Length,
                        DateTaken = info.CreationTime,
                        IsSelected = true,
                        Status = "Siap Export / Upload",
                        StatusCategory = "Ready"
                    });
                }
            });

            return items;
        }

        public async Task<string> DownloadFileAsync(CameraMediaItem item, string stagingDir)
        {
            Directory.CreateDirectory(stagingDir);
            string localPath = Path.Combine(stagingDir, item.FileName);
            await Task.Run(() => File.Copy(item.RemotePath, localPath, overwrite: true));
            return localPath;
        }

        public void Disconnect()
        {
            // Tidak ada koneksi hardware yang perlu dilepas untuk folder lokal
        }
    }
}
