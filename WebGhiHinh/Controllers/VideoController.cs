using ClosedXML.Excel; // Thư viện Excel
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/videos")] // Lưu ý: React gọi api/videos
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly AppDbContext _context;

        public VideoController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/videos (Có lọc theo ngày, QR, Camera)
        [HttpGet]
        public async Task<IActionResult> GetVideos(
            [FromQuery] string? qr_code,
            [FromQuery] string? date_from,
            [FromQuery] string? date_to,
            [FromQuery] int? camera_id)
        {
            var query = BuildQuery(qr_code, date_from, date_to, camera_id);

            // Sắp xếp mới nhất trước
            var videos = await query.OrderByDescending(v => v.StartTime).ToListAsync();
            return Ok(videos);
        }

        // GET: api/videos/export (Xuất Excel)
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

                // Tiêu đề
                worksheet.Cell(1, 1).Value = "STT";
                worksheet.Cell(1, 2).Value = "Mã QR";
                worksheet.Cell(1, 3).Value = "Người ghi";
                worksheet.Cell(1, 4).Value = "Trạm";
                worksheet.Cell(1, 5).Value = "Bắt đầu";
                worksheet.Cell(1, 6).Value = "Kết thúc";
                worksheet.Cell(1, 7).Value = "File Video";

                // Tô đậm tiêu đề
                var headerRange = worksheet.Range("A1:G1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

                // Đổ dữ liệu
                for (int i = 0; i < videos.Count; i++)
                {
                    var v = videos[i];
                    worksheet.Cell(i + 2, 1).Value = i + 1;
                    worksheet.Cell(i + 2, 2).Value = v.QrCode;
                    worksheet.Cell(i + 2, 3).Value = v.RecordedBy;
                    worksheet.Cell(i + 2, 4).Value = v.StationName;
                    worksheet.Cell(i + 2, 5).Value = v.StartTime;
                    worksheet.Cell(i + 2, 6).Value = v.EndTime.HasValue ? v.EndTime.Value : "Đang ghi...";
                    worksheet.Cell(i + 2, 7).Value = v.FilePath;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCao_Video_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
        }

        // DELETE: api/videos/5 (Chỉ Admin mới xóa được)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteVideo(int id)
        {
            var video = await _context.VideoLogs.FindAsync(id);
            if (video == null) return NotFound();

            // Xóa file vật lý (Nếu cần)
            try
            {
                if (System.IO.File.Exists(video.FilePath)) System.IO.File.Delete(video.FilePath);
            }
            catch { }

            _context.VideoLogs.Remove(video);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã xóa video" });
        }

        // Hàm phụ trợ tạo Query lọc dữ liệu
        private IQueryable<VideoLog> BuildQuery(string? qr, string? dFrom, string? dTo, int? camId)
        {
            var query = _context.VideoLogs.AsQueryable();

            if (!string.IsNullOrEmpty(qr))
                query = query.Where(v => v.QrCode.Contains(qr));

            if (DateTime.TryParse(dFrom, out DateTime df))
                query = query.Where(v => v.StartTime >= df);

            if (DateTime.TryParse(dTo, out DateTime dt))
                query = query.Where(v => v.StartTime < dt.AddDays(1)); // Đến hết ngày đó

            if (camId.HasValue)
                query = query.Where(v => v.CameraId == camId);

            return query;
        }
    }
}