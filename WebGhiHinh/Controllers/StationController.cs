using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using WebGhiHinh.Models.Dto;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StationController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StationController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1) LẤY DANH SÁCH TRẠM (TRẢ CẢ 2 CAMERA)
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

            var result = stations.Select(s => new StationDto
            {
                Id = s.Id,
                Name = s.Name,
                CurrentUserId = s.CurrentUserId,
                CurrentUsername = s.CurrentUser?.Username,
                OverviewCameraId = s.OverviewCameraId,
                QrCameraId = s.QrCameraId,
                OverviewCamera = s.OverviewCamera == null ? null : new CameraMiniDto
                {
                    Id = s.OverviewCamera.Id,
                    Name = s.OverviewCamera.Name,
                    RtspUrl = s.OverviewCamera.RtspUrl,
                    Description = s.OverviewCamera.Description
                },
                QrCamera = s.QrCamera == null ? null : new CameraMiniDto
                {
                    Id = s.QrCamera.Id,
                    Name = s.QrCamera.Name,
                    RtspUrl = s.QrCamera.RtspUrl,
                    Description = s.QrCamera.Description
                }
            }).ToList();

            return Ok(result);
        }

        // ==========================================
        // 2) LẤY 1 TRẠM THEO ID (CHI TIẾT + 2 CAMERA)
        // ==========================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetStationById(int id)
        {
            var s = await _context.Stations
                .Include(x => x.OverviewCamera)
                .Include(x => x.QrCamera)
                .Include(x => x.CurrentUser)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound(new { message = "Không tìm thấy trạm" });

            var dto = new StationDto
            {
                Id = s.Id,
                Name = s.Name,
                CurrentUserId = s.CurrentUserId,
                CurrentUsername = s.CurrentUser?.Username,
                OverviewCameraId = s.OverviewCameraId,
                QrCameraId = s.QrCameraId,
                OverviewCamera = s.OverviewCamera == null ? null : new CameraMiniDto
                {
                    Id = s.OverviewCamera.Id,
                    Name = s.OverviewCamera.Name,
                    RtspUrl = s.OverviewCamera.RtspUrl,
                    Description = s.OverviewCamera.Description
                },
                QrCamera = s.QrCamera == null ? null : new CameraMiniDto
                {
                    Id = s.QrCamera.Id,
                    Name = s.QrCamera.Name,
                    RtspUrl = s.QrCamera.RtspUrl,
                    Description = s.QrCamera.Description
                }
            };

            return Ok(dto);
        }

        // ==========================================
        // 3) ADMIN GÁN 2 CAMERA CHO TRẠM
        // ==========================================
        [HttpPost("set-cameras")]
        public async Task<IActionResult> SetCameras([FromBody] SetStationCamerasRequest req)
        {
            if (req == null || req.StationId <= 0)
                return BadRequest(new { message = "Request không hợp lệ" });

            var station = await _context.Stations
                .FirstOrDefaultAsync(s => s.Id == req.StationId);

            if (station == null)
                return NotFound(new { message = "Station không tồn tại" });

            if (req.OverviewCameraId.HasValue)
            {
                var exists = await _context.Cameras.AnyAsync(c => c.Id == req.OverviewCameraId.Value);
                if (!exists)
                    return BadRequest(new { message = "OverviewCameraId không hợp lệ" });
            }

            if (req.QrCameraId.HasValue)
            {
                var exists = await _context.Cameras.AnyAsync(c => c.Id == req.QrCameraId.Value);
                if (!exists)
                    return BadRequest(new { message = "QrCameraId không hợp lệ" });
            }

            station.OverviewCameraId = req.OverviewCameraId;
            station.QrCameraId = req.QrCameraId;

            _context.Stations.Update(station);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đã cập nhật camera cho trạm.",
                stationId = station.Id,
                overviewCameraId = station.OverviewCameraId,
                qrCameraId = station.QrCameraId
            });
        }

        // ==========================================
        // 4) XÓA TRẠM (NẾU CẦN)
        // ==========================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteStation(int id)
        {
            var station = await _context.Stations.FindAsync(id);
            if (station == null)
                return NotFound(new { message = "Không tìm thấy trạm" });

            _context.Stations.Remove(station);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa trạm" });
        }
    }
}
