using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebGhiHinh.Hubs;

namespace WebGhiHinh.Controllers
{
    [Route("api/qr")]
    [ApiController]
    public class QrController : ControllerBase
    {
        private readonly IHubContext<ScanHub> _hubContext;

        public QrController(IHubContext<ScanHub> hubContext)
        {
            _hubContext = hubContext;
        }

        [HttpPost("scan")]
        [Authorize(AuthenticationSchemes = "Bearer")]
        public async Task<IActionResult> ReceiveScan([FromBody] ScanPayload payload)
        {
            // Log server
            Console.WriteLine($"🔔 API HIT: {payload.StationName} -> {payload.Text}");

            // 👇 QUAN TRỌNG: Phải khớp tên sự kiện với file JS: "ScanResult"
            // Tạm thời để x,y,w,h = 0 vì Worker chưa gửi tọa độ
            await _hubContext.Clients.All.SendAsync("ScanResult", new
            {
                station = payload.StationName, // JS dùng payload.station
                code = payload.Text,           // JS dùng payload.code
                x = 0,
                y = 0,
                w = 0,
                h = 0     // Dummy coordinates
            });

            return Ok();
        }
    }

    public class ScanPayload
    {
        public string Text { get; set; } = "";
        public string StationName { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}