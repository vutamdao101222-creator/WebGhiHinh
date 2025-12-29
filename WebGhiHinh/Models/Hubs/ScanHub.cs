using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using WebGhiHinh.Services; // ✅ Cần namespace này để gọi QrDispatchService

namespace WebGhiHinh.Hubs
{
    public class ScanHub : Hub
    {
        // 👇 1. Khai báo Service xử lý Logic
        private readonly QrDispatchService _dispatcher;

        // 👇 2. Inject Service vào Constructor
        public ScanHub(QrDispatchService dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            // Console.WriteLine($"[ScanHub] Client Connected: {Context.ConnectionId}");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        // =================================================================
        // 🔥 HÀM NHẬN DỮ LIỆU TỪ WORKER (QR Scan Service)
        // Worker gọi: await _hubConnection.InvokeAsync("PushScanResult", ...)
        // =================================================================
        public async Task PushScanResult(
            string station,
            string code,
            double x,
            double y,
            double w,
            double h
        )
        {
            // 🔹 BƯỚC 1: Cập nhật giao diện ngay lập tức (Visual Only)
            // Gửi tọa độ xuống Browser để vẽ khung xanh/đỏ đè lên Video
            await Clients.All.SendAsync("ScanResult",
                station,
                code,
                x, y, w, h
            );

            // 🔹 BƯỚC 2: Xử lý nghiệp vụ (Login / Ghi hình)
            // Gọi sang QrDispatchService để kiểm tra xem mã này là CTV hay Sản phẩm
            // Dùng Task.Run để chạy ngầm, tránh làm lag việc vẽ khung hình
            _ = Task.Run(() => _dispatcher.ProcessScanAsync(station, code));
        }

        // Optional: Hàm nhận log từ Client (Worker) để debug nếu cần
        public async Task SendLog(string message)
        {
            // Chuyển log xuống Browser (Console Log) để Admin theo dõi
            await Clients.All.SendAsync("ReceiveLog", message);
        }
    }
}