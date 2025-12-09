using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")] // => api/cameras
    [ApiController]
    public class CamerasController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CamerasController(AppDbContext context)
        {
            _context = context;
        }

        // ===============================
        // 1) Lấy danh sách camera
        // GET: api/cameras
        // ===============================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Camera>>> GetCameras()
        {
            return await _context.Cameras
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // ===============================
        // 1.1) Lấy 1 camera theo id (nên có để CreatedAtAction đúng)
        // GET: api/cameras/5
        // ===============================
        [HttpGet("{id}")]
        public async Task<ActionResult<Camera>> GetCamera(int id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            return camera;
        }

        // ===============================
        // 2) Thêm mới camera
        // POST: api/cameras
        // ===============================
        [HttpPost]
        public async Task<ActionResult<Camera>> PostCamera([FromBody] Camera camera)
        {
            if (camera == null) return BadRequest("Camera không hợp lệ.");

            _context.Cameras.Add(camera);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCamera), new { id = camera.Id }, camera);
        }

        // ===============================
        // 3) Cập nhật camera (fix 404)
        // PUT: api/cameras/5
        // ===============================
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCamera(int id, [FromBody] Camera camera)
        {
            if (camera == null) return BadRequest("Camera không hợp lệ.");
            if (id != camera.Id) return BadRequest("ID không khớp.");

            // Nếu muốn update an toàn hơn, có thể load entity rồi map
            // Ở đây giữ kiểu update nhanh:
            _context.Entry(camera).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CameraExists(id))
                    return NotFound($"Không tìm thấy Camera có ID = {id}");

                throw;
            }

            return NoContent();
        }

        // ===============================
        // 4) Xóa camera
        // DELETE: api/cameras/5
        // ===============================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCamera(int id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            _context.Cameras.Remove(camera);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CameraExists(int id)
        {
            return _context.Cameras.Any(e => e.Id == id);
        }
    }
}
