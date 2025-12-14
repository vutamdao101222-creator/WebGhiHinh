using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions; // <-- Đã có sau khi cài OpenCvSharp4.Extensions
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing; // Cần thiết cho Bitmap
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebGhiHinh.Hubs;
using ZXing.Windows.Compatibility; // <-- QUAN TRỌNG: Dùng cái này để sửa lỗi BarcodeReader<T>

namespace WebGhiHinh.Services
{
    public class CameraAiService : BackgroundService
    {
        private readonly IHubContext<ScanHub> _hubContext;
        private readonly ILogger<CameraAiService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        // Dictionary quản lý task của từng camera
        private ConcurrentDictionary<string, Task> _cameraTasks = new();

        public CameraAiService(IHubContext<ScanHub> hubContext, ILogger<CameraAiService> logger, IServiceScopeFactory scopeFactory)
        {
            _hubContext = hubContext;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 CAMERA AI SERVICE (BACKEND) ĐÃ KHỞI ĐỘNG...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // --- DANH SÁCH CAMERA (Thay bằng DB thật của bạn) ---
                    // Cấu trúc: (Tên trạm, Link RTSP)
                    var activeCameras = new List<(string Name, string Url)>
                    {
                        // Link test mẫu (nếu bạn chưa có cam thật thì dùng link file MP4 online để test)
                        // ("may1", "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"),
                        
                        // Link RTSP thật của bạn:
                        ("may1", "rtsp://admin:123456@192.168.1.48:554/cam/realmonitor?channel=1&subtype=0"),
                    };

                    foreach (var cam in activeCameras)
                    {
                        if (!_cameraTasks.ContainsKey(cam.Name))
                        {
                            var t = Task.Run(() => ProcessCameraLoop(cam.Name, cam.Url, stoppingToken), stoppingToken);
                            _cameraTasks.TryAdd(cam.Name, t);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Lỗi quản lý camera: {ex.Message}");
                }

                await Task.Delay(30000, stoppingToken); // Check lại sau 30s
            }
        }

        private async Task ProcessCameraLoop(string stationName, string rtspUrl, CancellationToken token)
        {
            _logger.LogInformation($"[{stationName}] Đang kết nối luồng: {rtspUrl} ...");

            // Khởi tạo ZXing Reader (Phiên bản Windows Compatibility)
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true // Cố gắng đọc mã mờ/nhòe
                }
            };

            while (!token.IsCancellationRequested)
            {
                VideoCapture capture = null;
                try
                {
                    capture = new VideoCapture(rtspUrl);

                    // Nếu là camera thật, bật TCP để hình ổn định
                    // capture.Set(VideoCaptureProperties.HwAcceleration, 1); 

                    if (!capture.IsOpened())
                    {
                        _logger.LogWarning($"[{stationName}] Không mở được luồng. Thử lại sau 5s...");
                        await Task.Delay(5000, token);
                        continue;
                    }

                    _logger.LogInformation($"[{stationName}] -> KẾT NỐI THÀNH CÔNG!");

                    using var mat = new Mat();
                    string lastCode = "";
                    DateTime lastScanTime = DateTime.MinValue;

                    while (!token.IsCancellationRequested && capture.IsOpened())
                    {
                        // Đọc khung hình
                        if (!capture.Read(mat) || mat.Empty())
                        {
                            _logger.LogWarning($"[{stationName}] Mất tín hiệu video.");
                            break;
                        }

                        // --- TỐI ƯU: GIẢM TẢI CPU ---
                        // Bỏ qua frame để chỉ xử lý khoảng 5-10 FPS
                        // capture.Grab(); // Uncomment dòng này để bỏ qua 1 frame

                        // Chuyển đổi: Mat (OpenCV) -> Bitmap (System.Drawing)
                        // Lệnh này cần OpenCvSharp4.Extensions
                        using var bitmap = BitmapConverter.ToBitmap(mat);

                        // Quét mã
                        var result = reader.Decode(bitmap);

                        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                        {
                            var code = result.Text;

                            // Debounce: Chỉ báo nếu là mã mới hoặc mã cũ đã qua 3 giây
                            if (code != lastCode || (DateTime.Now - lastScanTime).TotalSeconds > 3)
                            {
                                _logger.LogInformation($"✅ [{stationName}] QUÉT ĐƯỢC: {code}");

                                // Gửi SignalR xuống Web
                                await _hubContext.Clients.All.SendAsync(
                                    "OnScanResultFromServer",
                                    stationName,
                                    code,
                                    0.2, 0.4, 0.6, 0.2 // Tọa độ giả lập (X, Y, W, H) để vẽ khung
                                );

                                lastCode = code;
                                lastScanTime = DateTime.Now;
                            }
                        }

                        // Nghỉ 50ms (~20 FPS) để không ăn 100% CPU
                        await Task.Delay(50, token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{stationName}] Lỗi runtime: {ex.Message}");
                    await Task.Delay(5000, token);
                }
                finally
                {
                    capture?.Dispose();
                }
            }
        }
    }
}