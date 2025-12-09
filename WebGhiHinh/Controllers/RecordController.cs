using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.QrCode) ||
                string.IsNullOrWhiteSpace(request.StationName))
            {
                return BadRequest(new { message = "Thiếu QR / StationName" });
            }

            var raw = request.QrCode.Trim();

            bool isStopCode =
                raw.Equals("STOP", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("STOP RECORDING", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("@@STOP_RECORD@@", StringComparison.OrdinalIgnoreCase);

            // ✅ LẤY RTSP QUAY TỔNG THỂ TỪ DB
            var station = await _context.Stations
                .Include(s => s.OverviewCamera)
                .FirstOrDefaultAsync(s => s.Name == request.StationName);

            if (station?.OverviewCamera == null)
            {
                return BadRequest(new { message = "Trạm chưa gán OverviewCamera" });
            }

            var rtspOverview = station.OverviewCamera.RtspUrl;

            string currentUserName = User.FindFirst("name")?.Value
                                     ?? User.FindFirst(ClaimTypes.Name)?.Value
                                     ?? User.Identity?.Name
                                     ?? "UnknownUser";
            currentUserName = currentUserName.Replace(" ", "");

            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == request.StationName && v.EndTime == null);

            // ==========================================
            // 0) STOP CODE
            // ==========================================
            if (isStopCode)
            {
                // ✅ nếu không có video đang quay -> KHÔNG ĐƯỢC START "STOP RECORDING"
                if (activeLog == null)
                {
                    return Ok(new
                    {
                        action = "ignore",
                        message = "Không có video đang quay để dừng."
                    });
                }

                try { _ffmpegService.StopRecording(request.StationName); } catch { }

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    action = "stop",
                    message = $"Đã dừng ghi hình tại {request.StationName}",
                    recording_qr = activeLog.QrCode
                });
            }

            // ==========================================
            // 1) TRÙNG MÃ ĐANG QUAY
            // - BarcodeGun: STOP
            // - CameraAuto: IGNORE
            // ==========================================
            if (activeLog != null &&
                string.Equals(activeLog.QrCode, raw, StringComparison.OrdinalIgnoreCase))
            {
                if (request.Mode == ScanSourceMode.BarcodeGun)
                {
                    try { _ffmpegService.StopRecording(request.StationName); } catch { }

                    activeLog.EndTime = DateTime.Now;
                    _context.VideoLogs.Update(activeLog);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        action = "stop",
                        message = $"Đã dừng ghi hình mã {raw}",
                        recording_qr = raw
                    });
                }

                return Ok(new
                {
                    action = "ignore",
                    message = "Auto thấy lại mã đang quay → bỏ qua",
                    recording_qr = raw
                });
            }

            // ==========================================
            // 2) ĐANG QUAY MÃ KHÁC → STOP CŨ
            // ==========================================
            string message = "";
            if (activeLog != null)
            {
                try { _ffmpegService.StopRecording(request.StationName); } catch { }

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();

                message = $"Đã dừng mã cũ ({activeLog.QrCode}). ";
            }

            // ==========================================
            // 3) START MÃ MỚI (QUAY TỔNG THỂ)
            // ==========================================
            var relativeFilePath = _ffmpegService.StartRecording(
                rtspOverview,
                raw,
                request.StationName,
                currentUserName
            );

            var newLog = new VideoLog
            {
                QrCode = raw,
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
                message = message + $"Bắt đầu ghi hình: {raw}",
                recording_qr = raw
            });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop([FromBody] StopRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.StationName))
                return BadRequest(new { message = "Thiếu StationName" });

            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == request.StationName && v.EndTime == null);

            try { _ffmpegService.StopRecording(request.StationName); } catch { }

            if (activeLog != null)
            {
                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                action = "stop",
                message = $"Đã dừng ghi hình tại {request.StationName}",
                recording_qr = activeLog?.QrCode
            });
        }

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
                if (!string.IsNullOrEmpty(v.StationName) && !status.ContainsKey(v.StationName))
                    status[v.StationName] = v.QrCode;
            }

            return Ok(status);
        }
    }
}
