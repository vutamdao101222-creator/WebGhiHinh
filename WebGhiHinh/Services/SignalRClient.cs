using Microsoft.AspNetCore.SignalR.Client;

namespace WebGhiHinh.Services
{
    public class SignalRClient
    {
        private readonly HubConnection _hub;

        public SignalRClient(string hubUrl)
        {
            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hub.StartAsync();
        }

        // Hàm gửi kết quả tọa độ QR (ScanResult)
        public async Task SendScanResultAsync(string stationName, string code, double x, double y, double w, double h)
        {
            if (_hub.State == HubConnectionState.Connected)
            {
                await _hub.SendAsync("ScanResult", stationName, code, x, y, w, h);
            }
        }

        // Hàm gửi thông báo hệ thống (SystemNotification)
        // type: "info", "success", "error", "warning"
        public async Task SendSystemNotificationAsync(string stationName, string message, string type = "info")
        {
            if (_hub.State == HubConnectionState.Connected)
            {
                // Bạn cần đảm bảo bên Hub server có method này, hoặc dùng ScanResult với code đặc biệt
                // Ở đây mình dùng ScanResult với code là thông báo để đơn giản hóa
                await _hub.SendAsync("ScanResult", stationName, $"MSG:{type}:{message}", 0, 0, 0, 0);
            }
        }
    }
}