using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Services;
using System.Security.Claims;

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

        // =====================================================
        // 1) SCAN QR → Start or Stop Recording
        // =====================================================
        [HttpPost("scan")]
        public async Task<IActionResult> StartScan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrEmpty(request.QrCode) || string.IsNullOrEmpty(request.RtspUrl))
            {
                return BadRequest(new { message = "Thiếu thông tin QR hoặc RTSP URL" });
            }

            string currentUserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "System";
            string message = "";

            // Tìm video đang quay tại trạm
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == request.StationName && v.EndTime == null);

            // -----------------------------------------------------
            // CASE 1: QUÉT LẠI ĐÚNG MÃ → STOP VIDEO
            // -----------------------------------------------------
            if (activeLog != null && activeLog.QrCode == request.QrCode)
            {
                _ffmpegService.StopRecording(request.StationName);

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    action = "stop",
                    message = $"Đã dừng ghi hình mã {request.QrCode}",
                    recording_qr = request.QrCode
                });
            }

            // -----------------------------------------------------
            // CASE 2: CÓ VIDEO CŨ (KHÁC MÃ) → STOP CŨ, START MỚI
            // -----------------------------------------------------
            if (activeLog != null)
            {
                _ffmpegService.StopRecording(activeLog.QrCode);

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();

                message += $"Đã dừng mã cũ ({activeLog.QrCode}). ";
            }

            // -----------------------------------------------------
            // CASE 3: KHÔNG TRÙNG MÃ → BẮT ĐẦU VIDEO MỚI
            // -----------------------------------------------------
            try
            {
                string relativeFilePath = _ffmpegService.StartRecording(
                    request.RtspUrl,
                    request.QrCode,
                    request.StationName,
                    currentUserName
                );

                var newLog = new VideoLog
                {
                    QrCode = request.QrCode,
                    StationName = request.StationName,
                    FilePath = relativeFilePath,
                    StartTime = DateTime.Now,
                    RecordedBy = currentUserName
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

        // =====================================================
        // 2) STOP RECORDING (manual)
        // =====================================================
        [HttpPost("stop")]
        public async Task<IActionResult> StopScan([FromBody] StopRequest request)
        {
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.QrCode == request.QrCode && v.EndTime == null);

            _ffmpegService.StopRecording(request.QrCode);

            if (activeLog != null)
            {
                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();
            }

            return Ok(new { action = "stop", message = $"Đã dừng ghi hình mã {request.QrCode}" });
        }

        // =====================================================
        // 3) Check trạm đang quay mã nào
        // =====================================================
        [HttpGet("recording-status")]
        public async Task<IActionResult> GetRecordingStatus()
        {
            var activeVideos = await _context.VideoLogs
                .Where(v => v.EndTime == null)
                .Select(v => new { v.StationName, v.QrCode })
                .ToListAsync();

            var status = new Dictionary<string, string>();

            foreach (var v in activeVideos)
            {
                if (!string.IsNullOrEmpty(v.StationName))
                    status[v.StationName] = v.QrCode;
            }

            return Ok(status);
        }
    }


    // DTOs
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
