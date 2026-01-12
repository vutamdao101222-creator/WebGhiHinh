using System.Collections.Concurrent;
using System.Diagnostics;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO; // Cần thiết cho Path và Directory

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        // Class lưu trạng thái tiến trình quay
        private sealed class StationProcess
        {
            public Process Process { get; init; } = default!;
            public string FileFullPath { get; init; } = "";
            public string User { get; init; } = "";
            public string QrCode { get; init; } = "";
            public DateTime StartedAt { get; init; } = DateTime.Now;
        }

        private readonly ConcurrentDictionary<string, StationProcess> _processes = new();
        private readonly ILogger<FfmpegService> _logger;
        private readonly string _recordingRoot;
        private readonly string _ffmpegExe;
        private readonly IServiceProvider _serviceProvider;

        // Tự động dừng sau 60 phút để tránh quên
        private const int AUTO_STOP_MINUTES = 60;

        public FfmpegService(
            ILogger<FfmpegService> logger,
            IConfiguration config,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // Lấy cấu hình đường dẫn
            _recordingRoot = config["Recording:Root"] ?? @"C:\GhiHinhVideos";
            _ffmpegExe = config["Recording:FfmpegPath"] ?? "ffmpeg";

            try
            {
                if (!Directory.Exists(_recordingRoot)) Directory.CreateDirectory(_recordingRoot);
            }
            catch (Exception ex)
            {
                _logger.LogError("Không thể tạo thư mục gốc: " + ex.Message);
            }
        }

        // ==========================================
        // 1️⃣ BẮT ĐẦU GHI HÌNH (Cấu hình ghép 2 cam ngang)
        // ==========================================
        public string StartRecording(string stationName, string username, string qrCode, string rtspOverview, string? rtspQr = null)
        {
            if (string.IsNullOrWhiteSpace(rtspOverview)) throw new ArgumentException("Cần ít nhất RTSP Overview");

            // Dừng tiến trình cũ nếu đang quay dở
            StopRecording(stationName);

            var safeUser = MakeSafe(username);
            var safeQr = MakeSafe(qrCode);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var folder = Path.Combine(_recordingRoot, MakeSafe(stationName));
            Directory.CreateDirectory(folder);

            // Dùng .mp4 để xem được trên Web (Vì đã có fix Soft Stop nên không sợ hỏng)
            var fileName = $"{safeUser}_{safeQr}_{ts}.mp4";
            var fullPath = Path.Combine(folder, fileName);

            string args;
            if (!string.IsNullOrWhiteSpace(rtspQr))
            {
                // 🔥 GHÉP NGANG 2 CAMERA (Side-by-Side)
                // - [1:v][0:v]scale2ref...: Chỉnh chiều cao cam QR bằng cam Toàn Cảnh để không lỗi
                // - hstack: Ghép 2 video theo chiều ngang
                // - ultrafast: Giảm tải CPU tối đa
                args = $"-hide_banner -loglevel error " +
                       $"-rtsp_transport tcp -i \"{rtspOverview}\" " + // Input 0
                       $"-rtsp_transport tcp -i \"{rtspQr}\" " +       // Input 1
                       $"-filter_complex \"[1:v][0:v]scale2ref=oh*mdar:ih[v1][v0];[v0][v1]hstack\" " +
                       $"-c:v libx264 -preset ultrafast -crf 28 -pix_fmt yuv420p -an \"{fullPath}\"";
            }
            else
            {
                // 🔥 QUAY 1 CAMERA (Copy luồng - Siêu nhẹ)
                args = $"-hide_banner -loglevel error -rtsp_transport tcp -i \"{rtspOverview}\" -c:v copy -an \"{fullPath}\"";
            }

            var p = StartFfmpegProcess(args, $"REC-{stationName}");

            var proc = new StationProcess
            {
                Process = p,
                FileFullPath = fullPath,
                User = username,
                QrCode = qrCode,
                StartedAt = DateTime.Now
            };

            _processes[stationName] = proc;

            // Timer tự động tắt nếu quên
            Task.Run(async () => {
                await Task.Delay(TimeSpan.FromMinutes(AUTO_STOP_MINUTES));
                if (_processes.ContainsKey(stationName))
                {
                    _logger.LogWarning($"[{stationName}] Tự động dừng do quá thời gian.");
                    StopRecording(stationName);
                }
            });

            _logger.LogInformation("🎬 [{Station}] Bắt đầu quay: {File}", stationName, fileName);

            // Trả về đường dẫn tương đối cho Web
            return Path.Combine("videos", MakeSafe(stationName), fileName).Replace("\\", "/");
        }

        // ==========================================
        // 2️⃣ DỪNG GHI HÌNH + LƯU DATABASE
        // ==========================================
        public void StopRecording(string stationName)
        {
            if (_processes.TryRemove(stationName, out var proc))
            {
                // 1. Dừng FFmpeg an toàn (Gửi lệnh 'q')
                StopProcessGracefully(proc.Process);
                _logger.LogInformation("🛑 [{Station}] Dừng ghi.", stationName);

                // 2. Lưu vào VideoLogs (Chạy ngầm để không block luồng chính)
                Task.Run(async () =>
                {
                    try
                    {
                        // Kiểm tra file có tồn tại không trước khi lưu
                        if (!File.Exists(proc.FileFullPath) || new FileInfo(proc.FileFullPath).Length == 0)
                        {
                            _logger.LogWarning($"[{stationName}] File video không tồn tại hoặc rỗng, bỏ qua lưu DB.");
                            return;
                        }

                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                            db.VideoLogs.Add(new VideoLog
                            {
                                StationName = stationName,
                                FilePath = proc.FileFullPath,
                                QrCode = proc.QrCode,
                                RecordedBy = proc.User,
                                StartTime = proc.StartedAt,
                                EndTime = DateTime.Now
                            });

                            await db.SaveChangesAsync();
                            _logger.LogInformation("💾 Đã lưu VideoLog thành công.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Lỗi lưu VideoLog");
                    }
                });
            }
        }

        // ==========================================
        // 3️⃣ CÁC HÀM HỖ TRỢ (CORE)
        // ==========================================

        private Process StartFfmpegProcess(string args, string logTag)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true // 👈 QUAN TRỌNG: Cho phép gửi lệnh 'q'
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // Log lỗi FFmpeg nếu có
            p.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.Contains("Error"))
                    _logger.LogWarning("[FFmpeg:{Tag}] {Msg}", logTag, e.Data);
            };

            p.Start();
            p.BeginErrorReadLine();
            return p;
        }

        // Hàm dừng tiến trình "êm ái" để không hỏng file MP4
        private void StopProcessGracefully(Process p)
        {
            try
            {
                if (!p.HasExited)
                {
                    // Gửi phím 'q' để FFmpeg tự đóng file (ghi Moov atom)
                    p.StandardInput.WriteLine("q");

                    // Đợi 5s, nếu chưa tắt thì mới Kill
                    if (!p.WaitForExit(5000)) p.Kill();
                }
            }
            catch { }
            finally { p.Dispose(); }
        }

        private static string MakeSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Unknown";
            foreach (var c in Path.GetInvalidFileNameChars()) input = input.Replace(c, '_');
            return input.Trim().Replace(" ", "_");
        }
    }
}