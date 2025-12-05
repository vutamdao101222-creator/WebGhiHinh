using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")] // api/cameras
    [ApiController]
    public class CamerasController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CamerasController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Camera>>> GetCameras()
        {
            return await _context.Cameras.ToListAsync();
        }

        // 2. Thêm mới
        [HttpPost]
        public async Task<ActionResult<Camera>> PostCamera(Camera camera)
        {
            _context.Cameras.Add(camera);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetCameras", new { id = camera.Id }, camera);
        }

        // 👇 3. CẬP NHẬT CAMERA (THÊM MỚI PHẦN NÀY ĐỂ SỬA LỖI 404)
        // PUT: api/cameras/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCamera(int id, Camera camera)
        {
            if (id != camera.Id)
            {
                return BadRequest("ID không khớp.");
            }

            _context.Entry(camera).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CameraExists(id))
                {
                    return NotFound($"Không tìm thấy Camera có ID = {id}");
                }
                else
                {
                    throw;
                }
            }

            return NoContent(); // Trả về 204 No Content khi thành công
        }

        // 4. Xóa
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