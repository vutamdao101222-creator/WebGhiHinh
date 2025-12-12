using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebGhiHinh.Hubs;
using WebGhiHinh.Models;

namespace WebGhiHinh.Services
{
    public class QrScanWorker : BackgroundService
    {
        private readonly ILogger<QrScanWorker> _logger;
        private readonly IServiceProvider _serviceProvider;

        // Demo: tạm cấu hình 2 camera
        private readonly List<(string StationName, string RtspUrl)> _cams = new()
        {
            ("máy1", "rtsp://.../Cam3"),
            ("máy2", "rtsp://.../CamX"),
        };

        public QrScanWorker(ILogger<QrScanWorker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("QrScanWorker started");

            var tasks = new List<Task>();
            foreach (var cam in _cams)
            {
                tasks.Add(RunCameraLoop(cam.StationName, cam.RtspUrl, stoppingToken));
            }

            await Task.WhenAll(tasks);
        }

        private async Task RunCameraLoop(string stationName, string rtspUrl, CancellationToken ct)
        {
            _logger.LogInformation("Start scan loop for {Station}", stationName);

            string? lastCode = null;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (hasCode, code, x, y, w, h) = await FakeScanAsync(rtspUrl, ct);

                    if (hasCode && !string.IsNullOrWhiteSpace(code))
                    {
                        if (!string.Equals(code, lastCode, StringComparison.Ordinal))
                        {
                            lastCode = code;

                            using var scope = _serviceProvider.CreateScope();
                            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ScanHub>>();

                            var msg = new ScanResultMessage
                            {
                                StationName = stationName,
                                Code = code,
                                X = x,
                                Y = y,
                                W = w,
                                H = h
                            };

                            // ✅ Gửi event ScanHit
                            await hub.Clients.All.SendAsync("ScanResult", msg, ct);

                            _logger.LogInformation("ScanWorker: {Station} => {Code}", stationName, code);

                            // TODO: nếu cần auto gọi RecordController / FfmpegService ở đây
                        }
                    }

                    await Task.Delay(300, ct);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scan loop {Station}", stationName);
                    await Task.Delay(1000, ct);
                }
            }

            _logger.LogInformation("Stop scan loop for {Station}", stationName);
        }

        /// <summary>
        /// FAKE: cứ 5 giây trả về 1 mã TEST123 ở giữa khung.
        /// Sau khi test xong pipeline, anh thay bằng scan thật (ZXing/OpenCV...).
        /// </summary>
        private Task<(bool HasCode, string Code, double X, double Y, double W, double H)> FakeScanAsync(
            string rtspUrl, CancellationToken ct)
        {
            var now = DateTime.Now;
            bool has = (now.Second % 5 == 0);

            if (!has)
            {
                return Task.FromResult<(bool, string, double, double, double, double)>(
                    (false, string.Empty, 0, 0, 0, 0)
                );
            }

            return Task.FromResult<(bool, string, double, double, double, double)>(
                (
                    true,
                    "TEST123",
                    0.3, // X
                    0.3, // Y
                    0.4, // W
                    0.4  // H
                )
            );
        }
    }
}
