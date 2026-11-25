using LensDrop.Models;
using MediaDevices;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public class UsbCameraSource : ICameraSource
    {
        private readonly MediaDevice _device;
        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".cr2", ".cr3", ".arw", ".nef", ".raf" };
        private static readonly string[] VideoExts = { ".mp4", ".mov", ".mxf", ".avi" };

        public UsbCameraSource(MediaDevice device)
        {
            _device = device;
        }

        public string DisplayName => $"{_device.FriendlyName} (USB)";
        public string ConnectionType => "USB";

        public async Task<List<CameraMediaItem>> ScanMediaAsync()
        {
            var items = new List<CameraMediaItem>();
            await Task.Run(() =>
            {
                if (!_device.IsConnected) _device.Connect();
                var root = _device.GetRootDirectory();
                var files = _device.EnumerateFiles(root.FullName, "*.*", SearchOption.AllDirectories);

                foreach (var path in files)
                {
                    string fileName = Path.GetFileName(path);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    bool isImg = ImageExts.Contains(ext);
                    bool isVid = VideoExts.Contains(ext);
                    if (!isImg && !isVid) continue;

                    var info = _device.GetFileInfo(path);
                    items.Add(new CameraMediaItem
                    {
                        RemotePath = path,
                        FileName = fileName,
                        Extension = ext.ToUpperInvariant(),
                        MediaType = isImg ? "Image" : "Video",
                        FileSizeBytes = (long)info.Length,
                        DateTaken = info.CreationTime ?? info.LastWriteTime,
                        IsSelected = true,
                        Status = "Siap Export",
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

            await Task.Run(() =>
            {
                if (!_device.IsConnected) _device.Connect();
                using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write);
                _device.DownloadFile(item.RemotePath, fs);
            });

            return localPath;
        }

        public void Disconnect()
        {
            if (_device.IsConnected) _device.Disconnect();
        }
    }
}
