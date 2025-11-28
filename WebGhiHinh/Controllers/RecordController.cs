using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Services;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RecordController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly FfmpegService _ffmpegService;

        public RecordController(AppDbContext context, FfmpegService ffmpegService)
        {
            _context = context;
            _ffmpegService = ffmpegService;
        }

        // ==========================================
        // 1. API BẮT ĐẦU GHI HÌNH (Quét QR)
        // POST: api/record/scan
        // ==========================================
        [HttpPost("scan")]
        public async Task<IActionResult> StartScan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrEmpty(request.QrCode) || string.IsNullOrEmpty(request.RtspUrl))
            {
                return BadRequest(new { message = "Thiếu thông tin QR hoặc RTSP URL" });
            }

            string message = "";

            // --- LOGIC 1: Kiểm tra xem TRẠM này có đang ghi video nào dở dang không? ---
            // Tìm video log nào của trạm này mà chưa có EndTime
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == request.StationName && v.EndTime == null);

            if (activeLog != null)
            {
                // Nếu đang ghi mã khác -> Dừng lại trước
                bool stopped = _ffmpegService.StopRecording(activeLog.QrCode);

                // Cập nhật EndTime vào DB
                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();

                message += $"Đã dừng mã cũ ({activeLog.QrCode}). ";
            }

            // --- LOGIC 2: Bắt đầu ghi hình MỚI ---
            try
            {
                // Gọi Service để chạy FFmpeg
                string fileName = _ffmpegService.StartRecording(request.RtspUrl, request.QrCode, request.StationName);

                // Lưu vào Database
                var newLog = new VideoLog
                {
                    QrCode = request.QrCode,
                    StationName = request.StationName,
                    FilePath = fileName,
                    StartTime = DateTime.Now,
                    RecordedBy = "System", // Hoặc lấy từ Token User nếu đã làm Auth
                    CameraId = null // Bạn có thể query để lấy CameraId nếu cần
                };

                _context.VideoLogs.Add(newLog);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    action = "start",
                    message = message + $"Bắt đầu ghi hình: {request.QrCode}",
                    recording_qr = request.QrCode
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // ==========================================
        // 2. API DỪNG GHI HÌNH (Thủ công hoặc Rời trạm)
        // POST: api/record/stop
        // ==========================================
        [HttpPost("stop")]
        public async Task<IActionResult> StopScan([FromBody] StopRequest request)
        {
            // Tìm log đang chạy trong DB
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.QrCode == request.QrCode && v.EndTime == null);

            if (activeLog == null)
            {
                return NotFound(new { message = "Không tìm thấy video đang ghi cho mã này." });
            }

            // Gọi Service dừng tiến trình FFmpeg
            _ffmpegService.StopRecording(request.QrCode);

            // Update DB
            activeLog.EndTime = DateTime.Now;
            _context.VideoLogs.Update(activeLog);
            await _context.SaveChangesAsync();

            return Ok(new { action = "stop", message = "Đã dừng ghi hình." });
        }

        // ==========================================
        // 3. API LẤY TRẠNG THÁI (Để React hiển thị đèn đỏ)
        // GET: api/record/recording-status
        // ==========================================
        [HttpGet("recording-status")]
        public async Task<IActionResult> GetRecordingStatus()
        {
            // Lấy danh sách tất cả các video chưa kết thúc (EndTime là null)
            var activeVideos = await _context.VideoLogs
                .Where(v => v.EndTime == null)
                .Select(v => new { v.StationName, v.QrCode })
                .ToListAsync();

            // Chuyển đổi thành Dictionary: { "Tram 1": "QR123", "Tram 2": "QR456" }
            // Để Frontend dễ dàng map status[station.name]
            var statusDict = new Dictionary<string, string>();
            foreach (var item in activeVideos)
            {
                if (!string.IsNullOrEmpty(item.StationName))
                {
                    statusDict[item.StationName] = item.QrCode;
                }
            }

            return Ok(statusDict);
        }
    }

    // --- CÁC CLASS DTO (Mô hình dữ liệu gửi lên) ---
    public class ScanRequest
    {
        public string QrCode { get; set; }
        public string RtspUrl { get; set; }
        public string StationName { get; set; }
    }

    public class StopRequest
    {
        public string QrCode { get; set; }
    }
}