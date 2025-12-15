using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Services;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RecordController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly FfmpegService _ffmpeg;

        public RecordController(AppDbContext context, FfmpegService ffmpeg)
        {
            _context = context;
            _ffmpeg = ffmpeg;
        }

        // =========================================================
        // 1) SCAN ENTRYPOINT
        // POST api/record/scan
        // Trả JSON:
        // { action: start|stop|ignore|error|station_join|station_leave|station_blocked, message, recording_qr? }
        // =========================================================
        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ScanRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.QrCode) || string.IsNullOrWhiteSpace(req.StationName))
                return BadRequest(new { action = "error", message = "Request không hợp lệ." });

            var code = req.QrCode.Trim();
            var stationName = req.StationName.Trim();

            // đảm bảo trạm tồn tại + load cam ids
            var station = await _context.Stations
                .Include(s => s.OverviewCamera)
                .Include(s => s.QrCamera)
                .Include(s => s.CurrentUser)
                .FirstOrDefaultAsync(s => s.Name == stationName);

            if (station == null)
                return NotFound(new { action = "error", message = "Không tìm thấy trạm." });

            // =====================================================
            // (A) ƯU TIÊN 1: QR NHÂN VIÊN (TOGGLE OCCUPY/RELEASE)
            // =====================================================
            var employee = await TryResolveEmployeeAsync(code);
            if (employee != null)
            {
                // Trạm trống -> gán user
                if (station.CurrentUserId == null)
                {
                    station.CurrentUserId = employee.Id;
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        action = "station_join",
                        message = $"✅ {employee.FullName ?? employee.Username} đã vào trạm.",
                        user = employee.Username
                    });
                }

                // Cùng user -> toggle rời trạm
                if (station.CurrentUserId == employee.Id)
                {
                    // nếu đang quay thì stop luôn
                    var activeLog = await _context.VideoLogs
                        .FirstOrDefaultAsync(v => v.StationName == station.Name && v.EndTime == null);

                    if (activeLog != null)
                    {
                        await StopActiveRecording(station.Name, activeLog);
                    }

                    station.CurrentUserId = null;
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        action = "station_leave",
                        message = $"🔓 {employee.FullName ?? employee.Username} đã rời trạm.",
                        user = employee.Username
                    });
                }

                // Trạm đang có người khác
                var other = await _context.Users.FirstOrDefaultAsync(u => u.Id == station.CurrentUserId);
                return Ok(new
                {
                    action = "station_blocked",
                    message = $"⛔ Trạm đang được dùng bởi {(other?.FullName ?? other?.Username ?? "người khác")}."
                });
            }

            // =====================================================
            // (B) ƯU TIÊN 2: LOGIC QUAY THEO ORDER + STOP CODE
            // =====================================================

            // active log của trạm
            var active = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == stationName && v.EndTime == null);

            // ------------- STOP LOGIC -------------
            if (IsStopCode(code))
            {
                if (active == null)
                {
                    return Ok(new
                    {
                        action = "ignore",
                        message = "Không có phiên quay để dừng."
                    });
                }

                await StopActiveRecording(station.Name, active);

                return Ok(new
                {
                    action = "stop",
                    message = "Đã dừng quay.",
                    recording_qr = active.QrCode
                });
            }

            // ------------- START LOGIC -------------
            if (active != null)
            {
                if (string.Equals(active.QrCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(new
                    {
                        action = "ignore",
                        message = "Auto thấy lại mã đang quay → bỏ qua.",
                        recording_qr = active.QrCode
                    });
                }

                return Ok(new
                {
                    action = "ignore",
                    message = $"Trạm đang quay mã khác: {active.QrCode}",
                    recording_qr = active.QrCode
                });
            }

            // Mã không phải stop, không phải nhân viên, cũng không phải order hợp lệ -> bỏ
            if (!IsOrderLike(code))
            {
                return Ok(new
                {
                    action = "ignore",
                    message = "Mã không hợp lệ để bắt đầu quay."
                });
            }

            // chọn camera ghi hình (ưu tiên Overview)
            string rtsp = req.RtspUrl ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rtsp))
            {
                rtsp = station.OverviewCamera?.RtspUrl
                       ?? station.QrCamera?.RtspUrl
                       ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(rtsp))
            {
                return BadRequest(new
                {
                    action = "error",
                    message = "Không có RTSP để ghi hình."
                });
            }

            var username = User.Identity?.Name ?? "Admin";

            // START FFMPEG -> nhận FilePath
            string filePath;
            try
            {
                filePath = _ffmpeg.StartRecording(rtsp, code, stationName, username);
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    action = "error",
                    message = "Start FFmpeg thất bại: " + ex.Message
                });
            }

            // tạo log (khớp đúng model VideoLog của bạn)
            var log = new VideoLog
            {
                QrCode = code,
                FilePath = filePath,
                StationName = stationName,
                RecordedBy = username,
                CameraId = station.OverviewCameraId ?? station.QrCameraId,
                StartTime = DateTime.Now,
                EndTime = null
            };

            _context.VideoLogs.Add(log);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                action = "start",
                message = "Đã bắt đầu quay.",
                recording_qr = code
            });
        }

        // =========================================================
        // 2) RECORDING STATUS
        // GET api/record/recording-status
        // Trả Dictionary<StationName, QrCode>
        // =========================================================
        [HttpGet("recording-status")]
        public async Task<IActionResult> GetRecordingStatus()
        {
            var actives = await _context.VideoLogs
                .Where(v => v.EndTime == null && v.StationName != null)
                .ToListAsync();

            var dict = actives
                .GroupBy(x => x.StationName!)
                .ToDictionary(g => g.Key, g => g.First().QrCode);

            return Ok(dict);
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private static bool IsStopCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            return code.Contains("STOP RECORDING", StringComparison.OrdinalIgnoreCase)
                || code.Equals("STOP", StringComparison.OrdinalIgnoreCase)
                || code.Contains("@@STOP_RECORD@@", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOrderLike(string code)
        {
            // Chỉ nhận chuỗi toàn số, dài >= 6: 000123, 456789...
            return Regex.IsMatch(code ?? "", @"^\d{6,}$");
        }

        // Nhận dạng QR nhân viên:
        //  - EMP:CODE  → cắt "EMP:"
        //  - hoặc CODE trùng EmployeeCode
        //  - hoặc CODE trùng Username
        // Hỗ trợ cả CTV0013 / CTV 0013 / ctv0013 ...
        private async Task<User?> TryResolveEmployeeAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var code = raw.Trim();
            string key = code;

            if (code.StartsWith("EMP:", StringComparison.OrdinalIgnoreCase))
                key = code.Substring(4).Trim();

            if (string.IsNullOrWhiteSpace(key)) return null;

            // Chuẩn hóa: bỏ khoảng trắng, upper-case
            var normalizedKey = key.Replace(" ", "").ToUpper();

            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    (u.EmployeeCode != null &&
                     u.EmployeeCode.Replace(" ", "").ToUpper() == normalizedKey)
                    ||
                    (u.Username != null &&
                     u.Username.Replace(" ", "").ToUpper() == normalizedKey)
                );

            return user;
        }

        private async Task StopActiveRecording(string stationName, VideoLog active)
        {
            try
            {
                _ffmpeg.StopRecording(stationName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Stop FFmpeg failed: {ex.Message}");
            }

            active.EndTime = DateTime.Now;
            _context.VideoLogs.Update(active);
            await _context.SaveChangesAsync();
        }
    }

    // =========================================================
    // DTOs + MODE
    // =========================================================
    public class ScanRequest
    {
        public string QrCode { get; set; } = "";
        public string RtspUrl { get; set; } = "";
        public string StationName { get; set; } = "";
        public ScanSourceMode Mode { get; set; } = ScanSourceMode.BarcodeGun;
    }

    public enum ScanSourceMode
    {
        BarcodeGun = 0,
        CameraAuto = 1
    }
}
