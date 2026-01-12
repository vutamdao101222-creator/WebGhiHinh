using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using WebGhiHinh.Services;

namespace WebGhiHinh.Hubs
{
    public class ScanHub : Hub
    {
        private readonly QrDispatchService _dispatcher;

        public ScanHub(QrDispatchService dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // 1. 🔥 MỚI: Hàm chuyên dụng cho Visual (Nhẹ, không logic)
        // Worker gọi cái này liên tục để vẽ khung xanh mượt mà
        public async Task PushVisual(string station, string code, double x, double y, double w, double h)
        {
            await Clients.All.SendAsync("ScanResult", station, code, x, y, w, h);
        }

        // 2. Hàm Logic (Nặng, có Dispatcher)
        // Worker gọi cái này 3s/lần để xử lý Login/Ghi hình
        public async Task PushScanResult(string station, string code, double x, double y, double w, double h)
        {
            // Vẫn vẽ lại 1 lần để chắc chắn đồng bộ
            await Clients.All.SendAsync("ScanResult", station, code, x, y, w, h);

            // Xử lý nghiệp vụ ngầm
            _ = Task.Run(() => _dispatcher.ProcessScanAsync(station, code));
        }
    }
}