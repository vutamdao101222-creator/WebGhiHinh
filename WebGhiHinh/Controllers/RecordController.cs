using Microsoft.AspNetCore.Mvc;
using WebGhiHinh.Models;
using WebGhiHinh.Workers;

namespace WebGhiHinh.Controllers
{
    [ApiController]
    [Route("api/record")]
    public class RecordController : ControllerBase
    {
        private readonly IQrScanQueue _qrQueue;

        public RecordController(IQrScanQueue qrQueue)
        {
            _qrQueue = qrQueue;
        }

        [HttpPost("scan")]
        public IActionResult Scan([FromBody] QrScanRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.QrCode))
                return BadRequest("QR code is required");

            _qrQueue.Enqueue(request);

            return Ok(new
            {
                message = "QR scan request queued",
                request.StationName,
                request.Mode
            });
        }
    }
}
