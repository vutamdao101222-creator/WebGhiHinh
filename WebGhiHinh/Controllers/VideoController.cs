using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/videos")]
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        // Đường dẫn gốc lưu video (Phải khớp với cấu hình trong Program.cs)
        private const string BaseVideoPath = @"C:\GhiHinhVideos";

        // File lưu cài đặt số ngày retention
        private readonly string _settingPath;

        public VideoController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
            _settingPath = Path.Combine(_env.ContentRootPath, "retention_settings.txt");
        }

        // ==========================================
        // 1. LẤY DANH SÁCH VIDEO (CÓ LỌC)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetVideos(
            [FromQuery] string? qr_code,
            [FromQuery] string? date_from,
            [FromQuery] string? date_to,
            [FromQuery] int? camera_id)
        {
            var query = BuildQuery(qr_code, date_from, date_to, camera_id);
            var videos = await query.OrderByDescending(v => v.StartTime).ToListAsync();
            return Ok(videos);
        }

        // ==========================================
        // 2. XÓA VIDEO (DATABASE + FILE VẬT LÝ)
        // ==========================================
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")] // Chỉ admin mới được xóa
        public async Task<IActionResult> DeleteVideo(int id)
        {
            var video = await _context.VideoLogs.FindAsync(id);
            if (video == null) return NotFound();

            // --- BƯỚC 1: XÓA FILE VẬT LÝ ---
            try
            {
                // Logic tìm đường dẫn file:
                // DB lưu dạng web: "videos/may1/abc.mp4" hoặc "may1/abc.mp4"
                // Cần map sang: "C:\GhiHinhVideos\may1\abc.mp4"

                if (!string.IsNullOrEmpty(video.FilePath) && !string.IsNullOrEmpty(video.StationName))
                {
                    string fileName = Path.GetFileName(video.FilePath);
                    string fullPath = Path.Combine(BaseVideoPath, video.StationName, fileName);

                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nhưng vẫn tiếp tục xóa DB để tránh rác dữ liệu
                Console.WriteLine($"[Error Deleting File] {ex.Message}");
            }

            // --- BƯỚC 2: XÓA DB ---
            _context.VideoLogs.Remove(video);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa video thành công" });
        }

        // ==========================================
        // 3. API CÀI ĐẶT (RETENTION SETTINGS)
        // ==========================================

        // Lấy số ngày cài đặt (Mặc định 30)
        [HttpGet("retention")]
        public async Task<ActionResult<int>> GetRetentionDays()
        {
            if (System.IO.File.Exists(_settingPath))
            {
                string content = await System.IO.File.ReadAllTextAsync(_settingPath);
                if (int.TryParse(content, out int days)) return days;
            }
            return 30;
        }

        // Lưu số ngày cài đặt
        [HttpPost("retention")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetRetentionDays([FromBody] int days)
        {
            if (days < 1) return BadRequest("Số ngày phải lớn hơn 0");
            await System.IO.File.WriteAllTextAsync(_settingPath, days.ToString());
            return Ok();
        }

        // ==========================================
        // 4. XUẤT EXCEL
        // ==========================================
        [HttpGet("export")]
        public async Task<IActionResult> ExportVideos(
            [FromQuery] string? qr_code,
            [FromQuery] string? date_from,
            [FromQuery] string? date_to,
            [FromQuery] int? camera_id)
        {
            var query = BuildQuery(qr_code, date_from, date_to, camera_id);
            var videos = await query.OrderByDescending(v => v.StartTime).ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("VideoLogs");

                // Header
                var headers = new[] { "STT", "Mã QR", "Người ghi", "Trạm", "Bắt đầu", "Kết thúc", "File Video" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Style Header
                var headerRange = worksheet.Range("A1:G1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

                // Data
                for (int i = 0; i < videos.Count; i++)
                {
                    var v = videos[i];
                    worksheet.Cell(i + 2, 1).Value = i + 1;
                    worksheet.Cell(i + 2, 2).Value = v.QrCode;
                    worksheet.Cell(i + 2, 3).Value = v.RecordedBy;
                    worksheet.Cell(i + 2, 4).Value = v.StationName;
                    worksheet.Cell(i + 2, 5).Value = v.StartTime;
                    worksheet.Cell(i + 2, 6).Value = v.EndTime.HasValue ? v.EndTime.Value.ToString() : "Đang ghi...";
                    worksheet.Cell(i + 2, 7).Value = v.FilePath;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCao_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                }
            }
        }

        // Helper Query
        private IQueryable<VideoLog> BuildQuery(string? qr, string? dFrom, string? dTo, int? camId)
        {
            var query = _context.VideoLogs.AsQueryable();

            if (!string.IsNullOrEmpty(qr))
                query = query.Where(v => v.QrCode.Contains(qr));

            if (DateTime.TryParse(dFrom, out DateTime df))
                query = query.Where(v => v.StartTime >= df);

            if (DateTime.TryParse(dTo, out DateTime dt))
                query = query.Where(v => v.StartTime < dt.AddDays(1));

            if (camId.HasValue)
                query = query.Where(v => v.CameraId == camId);

            return query;
        }
    }
}