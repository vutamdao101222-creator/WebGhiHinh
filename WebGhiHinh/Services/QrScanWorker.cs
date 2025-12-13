using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using WebGhiHinh.Data;
using WebGhiHinh.Hubs;
using WebGhiHinh.Models;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace WebGhiHinh.Services
{
    public sealed class QrScanWorker : BackgroundService
    {
        private readonly ILogger<QrScanWorker> _logger;
        private readonly IServiceProvider _services;
        private readonly IHttpClientFactory _httpClientFactory;

        // Cấu hình
        // Đã bỏ PROCESS_WIDTH để giữ nguyên độ phân giải gốc
        private const int SKIP_FRAMES = 2; // Vẫn giữ skip frame để tránh bị delay hình

        public QrScanWorker(
            ILogger<QrScanWorker> logger,
            IServiceProvider services,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _services = services;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 QR SCAN WORKER (FULL RESOLUTION) STARTING...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var cams = await db.Stations
                        .Include(s => s.QrCamera)
                        .Where(s => s.QrCameraId != null && s.QrCamera!.RtspUrl != null)
                        .Select(s => new { StationName = s.Name, Rtsp = s.QrCamera!.RtspUrl! })
                        .ToListAsync(stoppingToken);

                    if (cams.Count == 0)
                    {
                        _logger.LogWarning("QrScanWorker: No cameras found. Retrying in 10s...");
                        await Task.Delay(10000, stoppingToken);
                        continue;
                    }

                    var tasks = cams.Select(c => RunCameraLoopAsync(c.StationName, c.Rtsp, stoppingToken)).ToArray();
                    await Task.WhenAll(tasks);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "QrScanWorker: Critical error, retrying in 5s.");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task RunCameraLoopAsync(string stationName, string rtspUrl, CancellationToken ct)
        {
            _logger.LogInformation($"[{stationName}] Loop Start: {rtspUrl}");

            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                    TryHarder = true // Cố gắng đọc mã khó (mờ, nghiêng)
                }
            };

            string? lastCode = null;
            DateTime lastHitTime = DateTime.MinValue;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var cap = new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);
                    // Giảm buffer để hình ảnh luôn là mới nhất (Real-time)
                    cap.Set(VideoCaptureProperties.BufferSize, 1);

                    if (!cap.IsOpened())
                    {
                        _logger.LogWarning($"[{stationName}] Connect failed. Retry in 5s.");
                        await Task.Delay(5000, ct);
                        continue;
                    }

                    using var frame = new Mat();
                    using var grayFrame = new Mat(); // Frame đen trắng
                    int frameCounter = 0;

                    while (!ct.IsCancellationRequested && cap.IsOpened())
                    {
                        // Đọc frame (lệnh này giúp xóa buffer cũ)
                        if (!cap.Read(frame) || frame.Empty())
                        {
                            break;
                        }

                        // Vẫn giữ logic Skip Frames để giảm tải CPU 
                        // (Vì xử lý ảnh gốc Full HD rất nặng, không nên xử lý 30fps)
                        frameCounter++;
                        if (frameCounter % (SKIP_FRAMES + 1) != 0)
                        {
                            continue;
                        }

                        // 1. Chuyển sang ảnh xám (Grayscale) - Không Resize
                        // Giữ nguyên kích thước gốc để đọc được chi tiết nhỏ nhất
                        Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);

                        // 2. Convert to Bitmap & Decode
                        using Bitmap bmp = BitmapConverter.ToBitmap(grayFrame);
                        var (hasCode, code, box) = TryDecode(bmp, reader);

                        if (hasCode && !string.IsNullOrWhiteSpace(code))
                        {
                            code = code.Trim();

                            // Logic Debounce (chống spam)
                            if (!string.Equals(code, lastCode, StringComparison.OrdinalIgnoreCase) ||
                                (DateTime.UtcNow - lastHitTime) > TimeSpan.FromSeconds(2))
                            {
                                lastCode = code;
                                lastHitTime = DateTime.UtcNow;

                                // Tính tọa độ dựa trên kích thước ảnh gốc (bmp.Width = frame.Width)
                                var (nx, ny, nw, nh) = Normalize(box, bmp.Width, bmp.Height);

                                // Bắn SignalR
                                await BroadcastHitAsync(stationName, code, nx, ny, nw, nh, ct);

                                // Gọi API ghi hình
                                _ = TriggerRecordScanAsync(stationName, code, rtspUrl);

                                _logger.LogInformation($"✅ HIT [{stationName}]: {code}");
                            }
                        }

                        // Delay cực ngắn để nhường CPU
                        await Task.Delay(10, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{stationName}] Loop Error: {ex.Message}. Retry in 3s.");
                    await Task.Delay(3000, ct);
                }
            }
        }

        private static (bool HasCode, string Code, Rectangle Box) TryDecode(Bitmap bmp, BarcodeReader reader)
        {
            try
            {
                var result = reader.Decode(bmp);
                if (result == null || string.IsNullOrWhiteSpace(result.Text))
                    return (false, string.Empty, Rectangle.Empty);

                var points = result.ResultPoints;
                if (points == null || points.Length < 2)
                    return (true, result.Text, new Rectangle(0, 0, bmp.Width, bmp.Height));

                float minX = points.Min(p => p.X);
                float maxX = points.Max(p => p.X);
                float minY = points.Min(p => p.Y);
                float maxY = points.Max(p => p.Y);

                // Padding rộng ra chút cho đẹp
                int pad = 20;
                int x = Math.Max(0, (int)minX - pad);
                int y = Math.Max(0, (int)minY - pad);
                int w = Math.Min(bmp.Width - x, (int)(maxX - minX) + (pad * 2));
                int h = Math.Min(bmp.Height - y, (int)(maxY - minY) + (pad * 2));

                return (true, result.Text, new Rectangle(x, y, w, h));
            }
            catch { return (false, string.Empty, Rectangle.Empty); }
        }

        private static (double X, double Y, double W, double H) Normalize(Rectangle box, int w, int h)
        {
            if (w == 0 || h == 0) return (0, 0, 0, 0);
            // Chia cho kích thước ảnh gốc để ra tỉ lệ %
            return ((double)box.X / w, (double)box.Y / h, (double)box.Width / w, (double)box.Height / h);
        }

        private async Task BroadcastHitAsync(string stationName, string code, double x, double y, double w, double h, CancellationToken ct)
        {
            try
            {
                using var scope = _services.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ScanHub>>();
                await hub.Clients.All.SendAsync("ScanHit", stationName, code, x, y, w, h, ct);
            }
            catch (Exception ex) { _logger.LogError(ex, "SignalR Error"); }
        }

        private async Task TriggerRecordScanAsync(string stationName, string code, string rtspUrl)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("QrScan");
                var body = new { qrCode = code, rtspUrl, stationName, mode = 1 };
                var resp = await client.PostAsJsonAsync("api/record/scan", body);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"API Record Error: {resp.StatusCode}");
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "API Call Error"); }
        }
    }
}