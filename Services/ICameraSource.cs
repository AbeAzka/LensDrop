using LensDrop.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public interface ICameraSource
    {
        string DisplayName { get; }
        string ConnectionType { get; }
        Task<List<CameraMediaItem>> ScanMediaAsync();
        Task<string> DownloadFileAsync(CameraMediaItem item, string stagingDir);
        void Disconnect();
    }
}
