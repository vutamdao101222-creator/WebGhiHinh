using Microsoft.AspNetCore.SignalR;
using System; // 👈 Cần dòng này để dùng Exception
using System.Threading.Tasks;

namespace WebGhiHinh.Hubs
{
    public class ScanHub : Hub
    {
        // Hàm này để Client (JS) có thể gửi log về Server nếu cần (tùy chọn)
        public async Task SendLog(string message)
        {
            await Clients.All.SendAsync("ReceiveLog", message);
        }

        // Hàm này gọi khi Client kết nối thành công
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            // Console.WriteLine($"Client Connected: {Context.ConnectionId}");
        }

        // Hàm này gọi khi Client ngắt kết nối
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}