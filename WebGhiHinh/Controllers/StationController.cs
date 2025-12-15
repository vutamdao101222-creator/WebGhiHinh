using Microsoft.AspNetCore.Authorization;
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
    [Authorize]
    public class StationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly FfmpegService _ffmpeg;

        public StationController(AppDbContext context, FfmpegService ffmpeg)
        {
            _context = context;
            _ffmpeg = ffmpeg;
        }

        // ==========================================
        // 1) GET ALL STATIONS (FULL INFO)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetStations()
        {
            var stations = await _context.Stations
                .Include(s => s.OverviewCamera)
                .Include(s => s.QrCamera)
                .Include(s => s.CurrentUser)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return Ok(stations);
        }

        // ==========================================
        // 2) CREATE STATION (ADMIN)
        // ==========================================
        [HttpPost]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> CreateStation([FromBody] CreateStationRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { message = "Tên trạm không hợp lệ." });

            var name = req.Name.Trim();
            var exists = await _context.Stations.AnyAsync(x => x.Name == name);
            if (exists)
                return Conflict(new { message = $"Trạm '{name}' đã tồn tại." });

            var st = new Station { Name = name };

            _context.Stations.Add(st);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"✅ Đã tạo trạm {st.Name}", id = st.Id });
        }

        // ==========================================
        // 3) SET 2 CAMERAS (ADMIN)
        // ==========================================
        [HttpPost("set-cameras")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> SetCameras([FromBody] SetStationCamerasRequest req)
        {
            if (req == null || req.StationId <= 0)
                return BadRequest(new { message = "Request không hợp lệ." });

            var st = await _context.Stations.FirstOrDefaultAsync(x => x.Id == req.StationId);
            if (st == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            if (req.OverviewCameraId.HasValue &&
                !await _context.Cameras.AnyAsync(c => c.Id == req.OverviewCameraId.Value))
                return BadRequest(new { message = "OverviewCameraId không hợp lệ." });

            if (req.QrCameraId.HasValue &&
                !await _context.Cameras.AnyAsync(c => c.Id == req.QrCameraId.Value))
                return BadRequest(new { message = "QrCameraId không hợp lệ." });

            st.OverviewCameraId = req.OverviewCameraId;
            st.QrCameraId = req.QrCameraId;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "✅ Đã cập nhật 2 camera cho trạm.",
                st.Id,
                st.OverviewCameraId,
                st.QrCameraId
            });
        }

        // ==========================================
        // 4) OCCUPY STATION (MANUAL - ADMIN/USER)
        // Dùng khi bạn vẫn muốn click UI vào trạm
        // ==========================================
        [HttpPost("occupy")]
        public async Task<IActionResult> OccupyStation([FromBody] StationActionDto dto)
        {
            if (dto == null || dto.StationId <= 0)
                return BadRequest(new { message = "Request không hợp lệ." });

            var st = await _context.Stations.FindAsync(dto.StationId);
            if (st == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            var userId = GetUserIdFromToken();
            if (userId == null)
                return Unauthorized(new { message = "Token không hợp lệ." });

            if (st.CurrentUserId != null)
            {
                if (st.CurrentUserId == userId)
                    return Ok(new { message = "Bạn đang ngồi trạm này rồi." });

                return BadRequest(new { message = "Trạm đang có người khác sử dụng." });
            }

            st.CurrentUserId = userId;
            await _context.SaveChangesAsync();

            return Ok(new { message = "✅ Đã vào trạm thành công." });
        }

        // ==========================================
        // 5) RELEASE STATION (MANUAL)
        // ==========================================
        [HttpPost("release")]
        public async Task<IActionResult> ReleaseStation([FromBody] StationActionDto dto)
        {
            if (dto == null || dto.StationId <= 0)
                return BadRequest(new { message = "Request không hợp lệ." });

            var userId = GetUserIdFromToken();
            if (userId == null)
                return Unauthorized(new { message = "Token không hợp lệ." });

            return await PerformRelease(dto.StationId, force: false, requesterUserId: userId.Value);
        }

        // ==========================================
        // 6) FORCE RELEASE (ADMIN)
        // ==========================================
        [HttpPost("force-release")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> ForceRelease([FromBody] ForceReleaseRequest req)
        {
            if (req == null || req.StationId <= 0)
                return BadRequest(new { message = "Request không hợp lệ." });

            return await PerformRelease(req.StationId, force: true, requesterUserId: null);
        }

        // ==========================================
        // 7) DELETE STATION (ADMIN)
        // ==========================================
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> DeleteStation(int id)
        {
            var st = await _context.Stations.FindAsync(id);
            if (st == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            _context.Stations.Remove(st);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"🗑️ Đã xóa trạm {st.Name}" });
        }

        // ==========================================
        // PRIVATE HELPERS
        // ==========================================
        private int? GetUserIdFromToken()
        {
            var uidStr =
                User.FindFirst("Id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(uidStr, out int uid))
                return uid;

            return null;
        }

        private async Task<IActionResult> PerformRelease(int stationId, bool force, int? requesterUserId)
        {
            var st = await _context.Stations.FindAsync(stationId);
            if (st == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            if (!force)
            {
                if (requesterUserId == null || st.CurrentUserId != requesterUserId)
                    return Unauthorized(new { message = "Bạn không có quyền rời trạm này." });
            }

            // Stop recording nếu đang quay
            var activeLog = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == st.Name && v.EndTime == null);

            if (activeLog != null)
            {
                try
                {
                    _ffmpeg.StopRecording(st.Name);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Stop FFmpeg failed: {ex.Message}");
                }

                activeLog.EndTime = DateTime.Now;
                _context.VideoLogs.Update(activeLog);
            }

            st.CurrentUserId = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = $"🔓 Đã giải phóng trạm {st.Name}" });
        }
    }

    // ==========================================
    // REQUEST DTOs
    // ==========================================
    public class CreateStationRequest
    {
        public string Name { get; set; } = "";
    }

    public class SetStationCamerasRequest
    {
        public int StationId { get; set; }
        public int? OverviewCameraId { get; set; }
        public int? QrCameraId { get; set; }
    }

    public class StationActionDto
    {
        public int StationId { get; set; }
    }

    public class ForceReleaseRequest
    {
        public int StationId { get; set; }
    }
}
