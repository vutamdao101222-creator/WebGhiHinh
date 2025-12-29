using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebGhiHinh.Data;
using WebGhiHinh.DTOs;
using WebGhiHinh.Models;
using WebGhiHinh.Models.Dto;
using WebGhiHinh.Services;

namespace WebGhiHinh.Controllers
{
    [ApiController]
    [Route("api/station")]
    public class StationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly FfmpegService _ffmpeg;

        public StationController(AppDbContext context, FfmpegService ffmpeg)
        {
            _context = context;
            _ffmpeg = ffmpeg;
        }

        // ================= GET ALL (PUBLIC) =================
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetStations()
        {
            var stations = await _context.Stations
                .Include(s => s.OverviewCamera)
                .Include(s => s.QrCamera)
                .Include(s => s.CurrentUser)
                .OrderBy(s => s.Name)
                .Select(s => new StationDto
                {
                    Id = s.Id,
                    Name = s.Name,

                    CurrentUserId = s.CurrentUserId,
                    CurrentUsername = s.CurrentUser != null ? s.CurrentUser.Username : null,

                    OverviewCameraId = s.OverviewCameraId,
                    OverviewCamera = s.OverviewCamera == null ? null : new CameraMiniDto
                    {
                        Id = s.OverviewCamera.Id,
                        Name = s.OverviewCamera.Name,
                        RtspUrl = s.OverviewCamera.RtspUrl,
                        Description = s.OverviewCamera.Description
                    },

                    QrCameraId = s.QrCameraId,
                    QrCamera = s.QrCamera == null ? null : new CameraMiniDto
                    {
                        Id = s.QrCamera.Id,
                        Name = s.QrCamera.Name,
                        RtspUrl = s.QrCamera.RtspUrl,
                        Description = s.QrCamera.Description
                    }
                })
                .ToListAsync();

            return Ok(stations);
        }

        // ================= CREATE =================
        [HttpPost]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> CreateStation(CreateStationRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest();

            var name = req.Name.Trim();
            if (await _context.Stations.AnyAsync(x => x.Name == name))
                return Conflict();

            var st = new Station { Name = name };
            _context.Stations.Add(st);
            await _context.SaveChangesAsync();

            return Ok(st);
        }

        // ================= SET CAMERAS =================
        [HttpPost("set-cameras")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> SetCameras(SetStationCamerasRequest req)
        {
            var st = await _context.Stations.FindAsync(req.StationId);
            if (st == null) return NotFound();

            st.OverviewCameraId = req.OverviewCameraId;
            st.QrCameraId = req.QrCameraId;

            await _context.SaveChangesAsync();
            return Ok();
        }

        // ================= OCCUPY =================
        [HttpPost("occupy")]
        [Authorize]
        public async Task<IActionResult> Occupy(StationActionDto dto)
        {
            var st = await _context.Stations.FindAsync(dto.StationId);
            if (st == null) return NotFound();

            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (st.CurrentUserId != null && st.CurrentUserId != uid)
                return BadRequest();

            st.CurrentUserId = uid;
            await _context.SaveChangesAsync();

            return Ok();
        }

        // ================= RELEASE =================
        [HttpPost("release")]
        [Authorize]
        public async Task<IActionResult> Release(StationActionDto dto)
            => await PerformRelease(dto.StationId, false);

        // ================= FORCE RELEASE =================
        [HttpPost("force-release")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> ForceRelease(DTOs.ForceReleaseRequest req)
            => await PerformRelease(req.StationId, true);

        // ================= PRIVATE =================
        private int? GetUserId()
        {
            var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(v, out var id) ? id : null;
        }

        private async Task<IActionResult> PerformRelease(int stationId, bool force)
        {
            var st = await _context.Stations.FindAsync(stationId);
            if (st == null) return NotFound();

            var uid = GetUserId();
            if (!force && st.CurrentUserId != uid)
                return Unauthorized();

            var log = await _context.VideoLogs
                .FirstOrDefaultAsync(v => v.StationName == st.Name && v.EndTime == null);

            if (log != null)
            {
                _ffmpeg.StopRecording(st.Name);
                log.EndTime = DateTime.Now;
            }

            st.CurrentUserId = null;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
