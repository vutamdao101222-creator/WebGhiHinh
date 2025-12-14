using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Services;

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
        // GET: api/stations
        // ==========================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Station>>> GetStations()
        {
            // Include để lấy luôn thông tin Camera và Người đang ngồi
            return await _context.Stations
                .Include(s => s.Camera)
                .Include(s => s.CurrentUser)
                .ToListAsync();
        }

        // ==========================================
        // 2. LẤY CAMERA CHƯA ĐƯỢC GÁN (Để hiện trong dropdown lúc tạo trạm)
        // GET: api/stations/unassigned-cameras
        // ==========================================
        [HttpGet("unassigned-cameras")]
        public async Task<ActionResult<IEnumerable<Camera>>> GetUnassignedCameras()
        {
            // Logic: Lấy tất cả Camera MÀ ID của nó KHÔNG nằm trong danh sách CameraId của bảng Station
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
        // POST: api/stations
        // ==========================================
        [HttpPost]
        public async Task<ActionResult<Station>> CreateStation(CreateStationDto dto)
        {
            // Kiểm tra xem Camera này đã được gán cho trạm nào chưa
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
        // PUT: api/stations/5
        // ==========================================
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateStation(int id, CreateStationDto dto)
        {
            var station = await _context.Stations.FindAsync(id);
            if (station == null) return NotFound();

            // Nếu thay đổi camera, kiểm tra camera mới có bị trùng không
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
        // DELETE: api/stations/5
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
        // 6. VÀO TRẠM (CHIẾM TRẠM)
        // POST: api/stations/occupy
        // ==========================================
        [HttpPost("occupy")]
        public async Task<IActionResult> OccupyStation([FromBody] StationActionDto dto)
        {
            var station = await _context.Stations.FindAsync(dto.StationId);
            if (station == null) return NotFound(new { message = "Không tìm thấy trạm" });

            if (station.CurrentUserId != null)
            {
                return BadRequest(new { message = "Trạm này đang có người khác sử dụng!" });
            }

            // ⚠️ CHÚ Ý: Vì chưa làm Auth Token, ta sẽ lấy User đầu tiên trong DB để test
            // Sau này bạn sẽ dùng: int userId = int.Parse(User.FindFirst("Id").Value);
            var firstUser = await _context.Users.FirstOrDefaultAsync();
            if (firstUser == null) return BadRequest(new { message = "Chưa có User nào trong hệ thống. Hãy tạo User trước." });

            station.CurrentUserId = firstUser.Id;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã vào trạm thành công" });
        }

        // ==========================================
        // 7. RỜI TRẠM (QUAN TRỌNG: TỰ ĐỘNG DỪNG VIDEO)
        // POST: api/stations/release
        // ==========================================
        [HttpPost("release")]
        public async Task<IActionResult> ReleaseStation([FromBody] StationActionDto dto)
        {
            return await PerformRelease(dto.StationId);
        }

        // ==========================================
        // 8. GIẢI PHÓNG TRẠM (Admin Force Release)
        // POST: api/stations/force-release
        // ==========================================
        [HttpPost("force-release")]
        public async Task<IActionResult> ForceRelease([FromBody] StationActionDto dto)
        {
            return await PerformRelease(dto.StationId);
        }

        // --- HÀM DÙNG CHUNG CHO RELEASE & FORCE RELEASE ---
        private async Task<IActionResult> PerformRelease(int stationId)
        {
            var station = await _context.Stations.FindAsync(stationId);
            if (station == null) return NotFound(new { message = "Không tìm thấy trạm" });

            // 1. Kiểm tra xem trạm này có đang ghi hình dở dang không?
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == station.Name && v.EndTime == null);

            if (activeLog != null)
            {
                // 👉 Tự động tắt FFmpeg
                _ffmpegService.StopRecording(activeLog.QrCode);

                // Cập nhật DB
                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
            }

            // 2. Xóa người dùng khỏi trạm
            station.CurrentUserId = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã rời trạm và dừng các tác vụ ghi hình." });
        }
    }

    // --- DTO ---
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