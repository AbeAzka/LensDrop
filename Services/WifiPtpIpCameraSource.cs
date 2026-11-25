using LensDrop.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public class WifiPtpIpCameraSource : ICameraSource
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private TcpClient? _cmdClient;
        private TcpClient? _eventClient;
        private NetworkStream? _cmdStream;
        private uint _transactionId = 1;

        // Static GUID agar kamera Canon mengingat PC ini setelah pairing pertama kali
        private static readonly Guid ClientGuid = new("8A4F2C10-9B3E-4D7A-B1C2-5E6F7A8B9C0D");

        private const uint PTPIP_INIT_COMMAND_REQUEST = 0x00000001;
        private const uint PTPIP_INIT_COMMAND_ACK = 0x00000002;
        private const uint PTPIP_INIT_EVENT_REQUEST = 0x00000003;
        private const uint PTPIP_INIT_EVENT_ACK = 0x00000004;
        private const uint PTPIP_INIT_FAIL = 0x00000005;
        private const uint PTPIP_CMD_REQUEST = 0x00000006;
        private const uint PTPIP_CMD_RESPONSE = 0x00000007;
        private const uint PTPIP_START_DATA_PACKET = 0x00000009;
        private const uint PTPIP_DATA_PACKET = 0x0000000A;
        private const uint PTPIP_END_DATA_PACKET = 0x0000000C;

        private const ushort PTP_OP_OPEN_SESSION = 0x1002;
        private const ushort PTP_OP_GET_OBJECT_HANDLES = 0x1007;
        private const ushort PTP_OP_GET_OBJECT_INFO = 0x1008;
        private const ushort PTP_OP_GET_OBJECT = 0x1009;

        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".cr2", ".cr3", ".arw", ".nef", ".raf" };
        private static readonly string[] VideoExts = { ".mp4", ".mov", ".mxf", ".avi" };

        public WifiPtpIpCameraSource(string ipAddress, int port = 15740)
        {
            _ipAddress = ipAddress;
            _port = port;
        }

        public string DisplayName => $"Wi-Fi Camera ({_ipAddress})";
        public string ConnectionType => "Wi-Fi";

        private async Task EnsureConnectedAsync()
        {
            if (_cmdClient != null && _cmdClient.Connected && _cmdStream != null)
                return;

            _cmdClient = new TcpClient();
            var connectTask = _cmdClient.ConnectAsync(_ipAddress, _port);
            if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
            {
                _cmdClient.Dispose();
                _cmdClient = null;
                throw new TimeoutException($"Waktu koneksi ke IP {_ipAddress}:{_port} habis. Pastikan Wi-Fi kamera aktif dan berada di jaringan yang sama.");
            }
            await connectTask;
            _cmdStream = _cmdClient.GetStream();

            byte[] guid = ClientGuid.ToByteArray();
            byte[] hostNameBytes = Encoding.Unicode.GetBytes("LensDropPC\0");
            uint initLen = (uint)(8 + 16 + hostNameBytes.Length + 4);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(initLen);
                bw.Write(PTPIP_INIT_COMMAND_REQUEST);
                bw.Write(guid);
                bw.Write(hostNameBytes);
                bw.Write((uint)0x00010000);
                await _cmdStream.WriteAsync(ms.ToArray());
            }

            var (ackType, ackPayload) = await ReadPacketAsync(_cmdStream);
            if (ackType == PTPIP_INIT_FAIL || ackType != PTPIP_INIT_COMMAND_ACK || ackPayload.Length < 4)
            {
                throw new Exception(
                    "Kamera menolak koneksi PTP/IP.\n\n" +
                    "Checklist Canon Wi-Fi:\n" +
                    "1. Pilih menu 'Kendali jarak jauh (EOS Utility)' — bukan 'Hubungkan ke ponsel'.\n" +
                    "2. Pilih 'Tambah perangkat untuk dihubungkan' agar kamera masuk mode pairing.\n" +
                    "3. Jika muncul konfirmasi 'LensDropPC' di LCD kamera, tekan OK."
                );
            }

            uint connectionNumber = BinaryPrimitives.ReadUInt32LittleEndian(ackPayload.AsSpan(0, 4));

            _eventClient = new TcpClient();
            await _eventClient.ConnectAsync(_ipAddress, _port);
            var eventStream = _eventClient.GetStream();

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((uint)12);
                bw.Write(PTPIP_INIT_EVENT_REQUEST);
                bw.Write(connectionNumber);
                await eventStream.WriteAsync(ms.ToArray());
            }

            var (eventAckType, _) = await ReadPacketAsync(eventStream);
            if (eventAckType != PTPIP_INIT_EVENT_ACK)
                throw new Exception("Gagal membuka PTP/IP Event Channel.");

            await SendOperationAsync(PTP_OP_OPEN_SESSION, new uint[] { 1 });
        }

        public async Task<List<CameraMediaItem>> ScanMediaAsync()
        {
            await EnsureConnectedAsync();
            var results = new List<CameraMediaItem>();

            byte[] handlesData = await SendOperationAsync(PTP_OP_GET_OBJECT_HANDLES, new uint[] { 0xFFFFFFFF, 0, 0 });
            if (handlesData.Length < 4) return results;

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(handlesData.AsSpan(0, 4));
            for (int i = 0; i < count; i++)
            {
                uint handle = BinaryPrimitives.ReadUInt32LittleEndian(handlesData.AsSpan(4 + (i * 4), 4));
                byte[] infoData = await SendOperationAsync(PTP_OP_GET_OBJECT_INFO, new uint[] { handle });

                if (infoData.Length <= 52) continue;

                uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(infoData.AsSpan(8, 4));
                string fileName = ReadPtpString(infoData, 52);
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                string ext = Path.GetExtension(fileName).ToLowerInvariant();
                bool isImg = ImageExts.Contains(ext);
                bool isVid = VideoExts.Contains(ext);
                if (!isImg && !isVid) continue;

                results.Add(new CameraMediaItem
                {
                    RemotePath = handle.ToString(),
                    FileName = fileName,
                    Extension = ext.ToUpperInvariant(),
                    MediaType = isImg ? "Image" : "Video",
                    FileSizeBytes = compressedSize,
                    IsSelected = true,
                    Status = "Siap Export",
                    StatusCategory = "Ready"
                });
            }

            return results;
        }

        public async Task<string> DownloadFileAsync(CameraMediaItem item, string stagingDir)
        {
            await EnsureConnectedAsync();
            Directory.CreateDirectory(stagingDir);

            uint handle = uint.Parse(item.RemotePath);
            byte[] fileBytes = await SendOperationAsync(PTP_OP_GET_OBJECT, new uint[] { handle });

            string localPath = Path.Combine(stagingDir, item.FileName);
            await File.WriteAllBytesAsync(localPath, fileBytes);
            return localPath;
        }

        private async Task<byte[]> SendOperationAsync(ushort opCode, uint[] parameters)
        {
            if (_cmdStream == null) throw new InvalidOperationException("PTP/IP stream belum siap.");

            uint txId = _transactionId++;
            uint packetLen = (uint)(18 + (parameters.Length * 4));

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(packetLen);
                bw.Write(PTPIP_CMD_REQUEST);
                bw.Write((uint)1);
                bw.Write(opCode);
                bw.Write(txId);
                foreach (var p in parameters) bw.Write(p);
                await _cmdStream.WriteAsync(ms.ToArray());
            }

            using var dataBuffer = new MemoryStream();
            while (true)
            {
                var (pktType, payload) = await ReadPacketAsync(_cmdStream);

                if (pktType == PTPIP_START_DATA_PACKET)
                {
                    continue;
                }
                else if (pktType == PTPIP_DATA_PACKET || pktType == PTPIP_END_DATA_PACKET)
                {
                    if (payload.Length > 4)
                        dataBuffer.Write(payload, 4, payload.Length - 4);
                }
                else if (pktType == PTPIP_CMD_RESPONSE)
                {
                    break;
                }
            }

            return dataBuffer.ToArray();
        }

        private async Task<(uint PacketType, byte[] Payload)> ReadPacketAsync(NetworkStream stream)
        {
            byte[] header = new byte[8];
            await ReadExactAsync(stream, header, 8);

            uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            uint packetType = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

            int payloadLen = (int)totalLength - 8;
            byte[] payload = payloadLen > 0 ? new byte[payloadLen] : Array.Empty<byte>();
            if (payloadLen > 0)
                await ReadExactAsync(stream, payload, payloadLen);

            return (packetType, payload);
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Koneksi Wi-Fi ke kamera terputus.");
                offset += read;
            }
        }

        private static string ReadPtpString(byte[] data, int offset)
        {
            if (offset >= data.Length) return string.Empty;
            byte numChars = data[offset];
            if (numChars == 0) return string.Empty;

            int byteLen = (numChars - 1) * 2;
            if (offset + 1 + byteLen > data.Length) return string.Empty;

            return Encoding.Unicode.GetString(data, offset + 1, byteLen);
        }

        public void Disconnect()
        {
            _cmdStream?.Dispose();
            _cmdClient?.Dispose();
            _eventClient?.Dispose();
            _cmdClient = null;
            _eventClient = null;
        }
    }
}
