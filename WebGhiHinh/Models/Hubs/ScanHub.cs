using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using WebGhiHinh.Models;

namespace WebGhiHinh.Hubs
{
    public class ScanHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            // Sau này nếu muốn group theo station:
            // var stationName = Context.GetHttpContext()?.Request.Query["station"];
            // if (!string.IsNullOrWhiteSpace(stationName))
            //     Groups.AddToGroupAsync(Context.ConnectionId, stationName);
            return base.OnConnectedAsync();
        }

        // Cho phép debug gọi thử từ Postman
        public Task BroadcastScan(ScanResultMessage msg)
        {
            // ✅ Thống nhất event name: ScanHit
            return Clients.All.SendAsync("ScanHit", msg);
        }
    }
}
