using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Services;
using System.Security.Claims; // Cần thiết để đọc Token

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly FfmpegService _ffmpegService;

        public StationController(AppDbContext context, FfmpegService ffmpegService)
        {
            _context = context;
            _ffmpegService = ffmpegService;
        }

        // ==========================================
        // 1. LẤY DANH SÁCH TRẠM
        // ==========================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Station>>> GetStations()
        {
            return await _context.Stations
                .Include(s => s.Camera)
                .Include(s => s.CurrentUser)
                .ToListAsync();
        }

        // ==========================================
        // 2. LẤY CAMERA CHƯA ĐƯỢC GÁN
        // ==========================================
        [HttpGet("unassigned-cameras")]
        public async Task<ActionResult<IEnumerable<Camera>>> GetUnassignedCameras()
        {
            var assignedCameraIds = await _context.Stations
                .Where(s => s.CameraId != null)
                .Select(s => s.CameraId)
                .ToListAsync();

            var cameras = await _context.Cameras
                .Where(c => !assignedCameraIds.Contains(c.Id))
                .ToListAsync();

            return cameras;
        }

        // ==========================================
        // 3. TẠO TRẠM MỚI
        // ==========================================
        [HttpPost]
        public async Task<ActionResult<Station>> CreateStation(CreateStationDto dto)
        {
            if (dto.CameraId.HasValue)
            {
                bool isAssigned = await _context.Stations.AnyAsync(s => s.CameraId == dto.CameraId);
                if (isAssigned)
                {
                    return BadRequest(new { message = "Camera này đã được gán cho trạm khác!" });
                }
            }

            var station = new Station
            {
                Name = dto.Name,
                CameraId = dto.CameraId
            };

            _context.Stations.Add(station);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetStations), new { id = station.Id }, station);
        }

        // ==========================================
        // 4. CẬP NHẬT TRẠM
        // ==========================================
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateStation(int id, CreateStationDto dto)
        {
            var station = await _context.Stations.FindAsync(id);
            if (station == null) return NotFound();

            if (dto.CameraId.HasValue && dto.CameraId != station.CameraId)
            {
                bool isAssigned = await _context.Stations.AnyAsync(s => s.CameraId == dto.CameraId);
                if (isAssigned)
                {
                    return BadRequest(new { message = "Camera này đã được gán cho trạm khác!" });
                }
            }

            station.Name = dto.Name;
            station.CameraId = dto.CameraId;

            await _context.SaveChangesAsync();
            return Ok(station);
        }

        // ==========================================
        // 5. XÓA TRẠM
        // ==========================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteStation(int id)
        {
            var station = await _context.Stations.FindAsync(id);
            if (station == null) return NotFound();

            _context.Stations.Remove(station);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ==========================================
        // 6. VÀO TRẠM (ĐÃ SỬA LỖI USER)
        // ==========================================
        [HttpPost("occupy")]
        public async Task<IActionResult> OccupyStation([FromBody] StationActionDto dto)
        {
            var station = await _context.Stations.FindAsync(dto.StationId);
            if (station == null) return NotFound(new { message = "Không tìm thấy trạm" });

            // Kiểm tra xem trạm có người ngồi chưa
            if (station.CurrentUserId != null)
            {
                // Kiểm tra xem có phải chính người dùng hiện tại đang ngồi không (đề phòng F5)
                // Ưu tiên lấy ID từ Token
                var currentUserIdStr = User.FindFirst("Id")?.Value
                                       ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (currentUserIdStr != null && station.CurrentUserId.ToString() == currentUserIdStr)
                {
                    return Ok(new { message = "Bạn đang ngồi trạm này rồi." });
                }

                return BadRequest(new { message = "Trạm này đang có người khác sử dụng!" });
            }

            // --- QUAN TRỌNG: Lấy User ID thực từ Token ---
            var userIdClaim = User.FindFirst("Id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized(new { message = "Không tìm thấy thông tin xác thực. Vui lòng đăng nhập lại." });
            }

            if (!int.TryParse(userIdClaim, out int userId))
            {
                return BadRequest(new { message = "Lỗi định dạng User ID trong Token." });
            }

            // Gán người dùng thực vào trạm
            station.CurrentUserId = userId;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã vào trạm thành công" });
        }

        // ==========================================
        // 7. RỜI TRẠM
        // ==========================================
        [HttpPost("release")]
        public async Task<IActionResult> ReleaseStation([FromBody] StationActionDto dto)
        {
            return await PerformRelease(dto.StationId);
        }

        // ==========================================
        // 8. GIẢI PHÓNG TRẠM (Admin)
        // ==========================================
        [HttpPost("force-release")]
        public async Task<IActionResult> ForceRelease([FromBody] StationActionDto dto)
        {
            return await PerformRelease(dto.StationId);
        }

        // --- HÀM DÙNG CHUNG (CÓ TRY-CATCH) ---
        private async Task<IActionResult> PerformRelease(int stationId)
        {
            var station = await _context.Stations.FindAsync(stationId);
            if (station == null) return NotFound(new { message = "Không tìm thấy trạm" });

            // 1. Kiểm tra và dừng ghi hình nếu đang quay
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == station.Name && v.EndTime == null);

            if (activeLog != null)
            {
                try
                {
                    // Bao bọc try-catch để tránh lỗi nếu process đã chết hoặc key không tồn tại
                    _ffmpegService.StopRecording(activeLog.QrCode);
                }
                catch (Exception ex)
                {
                    // Log lỗi ra console server để debug, không làm gián đoạn việc rời trạm
                    Console.WriteLine($"[Warning] Lỗi dừng FFmpeg khi Release: {ex.Message}");
                }

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
            }

            // 2. Xóa người dùng khỏi trạm
            station.CurrentUserId = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã rời trạm." });
        }
    }

    // DTOs
    public class CreateStationDto
    {
        public string Name { get; set; }
        public int? CameraId { get; set; }
    }

    public class StationActionDto
    {
        public int StationId { get; set; }
    }
}