using System.Collections.Concurrent;
using System.Diagnostics;
using WebGhiHinh.DTOs;
using WebGhiHinh.Models;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        private sealed class StationProcess
        {
            public Process Process { get; init; } = default!;
            public string FileFullPath { get; init; } = "";
            public DateTime StartedAt { get; init; } = DateTime.Now;
        }

        private readonly ConcurrentDictionary<string, StationProcess> _processes = new();
        private readonly ILogger<FfmpegService> _logger;
        private readonly string _recordingRoot;
        private readonly string _ffmpegExe;

        public FfmpegService(ILogger<FfmpegService> logger, IConfiguration? config = null)
        {
            _logger = logger;
            // Chỉ cần cấu hình thư mục Ghi hình, không cần HLS nữa
            _recordingRoot = config?["Recording:Root"] ?? @"C:\GhiHinhVideos";
            _ffmpegExe = config?["Recording:FfmpegPath"] ?? "ffmpeg";

            try
            {
                if (!Directory.Exists(_recordingRoot)) Directory.CreateDirectory(_recordingRoot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể tạo thư mục lưu trữ video");
            }
        }

        // ==========================================
        // 1. CHỨC NĂNG GHI HÌNH (GIỮ NGUYÊN)
        // ==========================================
        // Logic này MediaMTX không làm thay được (ghép Cam QR vào Cam Chính), nên phải giữ lại.

        public string StartRecording(string stationName, string username, string qrCode, string rtspOverview, string? rtspQr = null)
        {
            if (string.IsNullOrWhiteSpace(rtspOverview)) throw new ArgumentException("Cần ít nhất RTSP Overview");

            StopRecording(stationName);

            var safeUser = MakeSafe(username);
            var safeQr = MakeSafe(qrCode);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var folder = Path.Combine(_recordingRoot, MakeSafe(stationName));
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var fileName = $"{safeUser}_{safeQr}_{ts}.mp4";
            var fullPath = Path.Combine(folder, fileName);

            string args;

            // Logic ghép 2 Cam (PiP) hoặc quay 1 Cam
            if (!string.IsNullOrWhiteSpace(rtspQr))
            {
                // GỘP 2 CAM: [1:v] là cam QR, scale nhỏ lại rồi overlay lên cam chính [0:v]
                args = $"-hide_banner -loglevel error -rtsp_transport tcp -i \"{rtspOverview}\" -rtsp_transport tcp -i \"{rtspQr}\" " +
                       $"-filter_complex \"[1:v]scale=iw/4:-1[pip];[0:v][pip]overlay=main_w-overlay_w-10:main_h-overlay_h-10\" " +
                       $"-c:v libx264 -preset ultrafast -crf 28 -pix_fmt yuv420p " +
                       $"-an \"{fullPath}\"";
            }
            else
            {
                // 1 CAM: Copy stream cho nhẹ
                args = $"-hide_banner -loglevel error -rtsp_transport tcp -i \"{rtspOverview}\" " +
                       $"-c:v copy -an \"{fullPath}\"";
            }

            var p = StartFfmpegProcess(args, $"REC-{stationName}");

            _processes[stationName] = new StationProcess
            {
                Process = p,
                FileFullPath = fullPath
            };

            return Path.Combine("videos", MakeSafe(stationName), fileName).Replace("\\", "/");
        }

        public void StopRecording(string stationName)
        {
            if (_processes.TryRemove(stationName, out var proc))
            {
                KillProcess(proc.Process);
            }
        }

        // ==========================================
        // 2. CHỨC NĂNG STREAM (ĐÃ XÓA BỎ)
        // ==========================================
        // MediaMTX đã lo việc này rồi. Code C# chỉ cần StartStream rỗng để không lỗi biên dịch
        // nếu lỡ có chỗ nào gọi đến nó.

        public Task StartStream(Camera? camera) => Task.CompletedTask; // Không làm gì cả
        public Task StartStream(CameraMiniDto? camDto) => Task.CompletedTask; // Không làm gì cả
        public Task StopStream(int cameraId) => Task.CompletedTask; // Không làm gì cả

        // ==========================================
        // HELPER FUNCTIONS
        // ==========================================

        private Process StartFfmpegProcess(string args, string logTag)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.ErrorDataReceived += (s, e) => {
                // Chỉ log lỗi nếu cần thiết
                if (!string.IsNullOrWhiteSpace(e.Data) && (e.Data.Contains("Error") || e.Data.Contains("Fail")))
                    _logger.LogWarning("[FFmpeg:{Tag}] {Msg}", logTag, e.Data);
            };

            p.Start();
            p.BeginErrorReadLine();
            return p;
        }

        private void KillProcess(Process p)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
            }
            catch { }
            finally { p.Dispose(); }
        }

        public Dictionary<string, string> GetRecordingStatus()
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in _processes)
            {
                result[kvp.Key] = Path.GetFileName(kvp.Value.FileFullPath);
            }
            return result;
        }

        private static string MakeSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Unknown";
            foreach (var c in Path.GetInvalidFileNameChars()) input = input.Replace(c, '_');
            return input.Trim().Replace(" ", "_");
        }
    }
}