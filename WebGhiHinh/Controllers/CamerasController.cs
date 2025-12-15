// FILE: Controllers/CamerasController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // nếu muốn public thì bỏ dòng này
    public class CamerasController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CamerasController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/cameras
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Camera>>> GetCameras()
        {
            return await _context.Cameras
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // GET: api/cameras/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Camera>> GetCamera(int id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();
            return camera;
        }

        // POST: api/cameras
        [HttpPost]
        [Authorize(Roles = "admin,admin1")]
        public async Task<ActionResult<Camera>> PostCamera([FromBody] Camera camera)
        {
            if (camera == null) return BadRequest("Camera không hợp lệ.");

            camera.Name = camera.Name?.Trim() ?? "";
            camera.RtspUrl = camera.RtspUrl?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(camera.Name) || string.IsNullOrWhiteSpace(camera.RtspUrl))
                return BadRequest("Thiếu Name hoặc RtspUrl.");

            _context.Cameras.Add(camera);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCamera), new { id = camera.Id }, camera);
        }

        // PUT: api/cameras/5
        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> PutCamera(int id, [FromBody] Camera camera)
        {
            if (camera == null) return BadRequest("Camera không hợp lệ.");
            if (id != camera.Id) return BadRequest("ID không khớp.");

            var exists = await _context.Cameras.FirstOrDefaultAsync(x => x.Id == id);
            if (exists == null) return NotFound($"Không tìm thấy Camera có ID = {id}");

            exists.Name = camera.Name?.Trim() ?? exists.Name;
            exists.RtspUrl = camera.RtspUrl?.Trim() ?? exists.RtspUrl;
            exists.Description = camera.Description;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/cameras/5
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin,admin1")]
        public async Task<IActionResult> DeleteCamera(int id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            _context.Cameras.Remove(camera);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
