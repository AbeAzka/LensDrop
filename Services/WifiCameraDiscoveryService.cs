using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LensDrop.Services
{
    public class WifiCameraDiscoveryService
    {
        public async Task<List<string>> DiscoverCameraIpsAsync(int timeoutMs = 1500)
        {
            var foundIps = new HashSet<string>();

            try
            {
                using var udp = new UdpClient();
                udp.EnableBroadcast = true;

                string ssdpRequest =
                    "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 1\r\n" +
                    "ST: ssdp:all\r\n\r\n";

                byte[] reqBytes = Encoding.ASCII.GetBytes(ssdpRequest);
                var multicastEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

                await udp.SendAsync(reqBytes, reqBytes.Length, multicastEndpoint);

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) break;

                    var receiveTask = udp.ReceiveAsync();
                    var completed = await Task.WhenAny(receiveTask, Task.Delay(remaining));
                    if (completed != receiveTask) break;

                    var result = await receiveTask;
                    string response = Encoding.ASCII.GetString(result.Buffer);

                    if (response.Contains("Canon", StringComparison.OrdinalIgnoreCase) ||
                        response.Contains("Sony", StringComparison.OrdinalIgnoreCase) ||
                        response.Contains("Nikon", StringComparison.OrdinalIgnoreCase) ||
                        response.Contains("FUJIFILM", StringComparison.OrdinalIgnoreCase) ||
                        response.Contains("Imaging", StringComparison.OrdinalIgnoreCase))
                    {
                        foundIps.Add(result.RemoteEndPoint.Address.ToString());
                    }
                }
            }
            catch
            {
                // Abaikan jika adapter jaringan tidak mendukung multicast SSDP
            }

            return new List<string>(foundIps);
        }
    }
}
