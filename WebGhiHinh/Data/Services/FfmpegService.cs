// ==========================================
// FILE: Services/FfmpegService.cs
// Lưu theo StationName vào C:\GhiHinhVideos
// Format file: username_qr_yyyyMMdd_HHmmss.mp4
// Trả về path dạng /videos/{station}/{file}.mp4
// ==========================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        private sealed class StationProcess
        {
            public Process Process { get; init; } = default!;
            public string FileFullPath { get; init; } = "";
            public DateTime StartedAt { get; init; } = DateTime.Now;
            public string QrCode { get; init; } = "";
            public string Username { get; init; } = "";
            public string RtspUrl { get; init; } = "";
        }

        private readonly ConcurrentDictionary<string, StationProcess> _stationProcesses = new();
        private readonly ILogger<FfmpegService> _logger;

        // Default root nếu không có cấu hình
        private const string DefaultRecordingRoot = @"C:\GhiHinhVideos";

        private readonly string _recordingRoot;
        private readonly string _ffmpegExe;

        public FfmpegService(ILogger<FfmpegService> logger, IConfiguration? config = null)
        {
            _logger = logger;

            // Cho phép override trong appsettings.json nếu bạn muốn:
            // "Recording": { "Root": "C:\\GhiHinhVideos", "FfmpegPath": "ffmpeg" }
            _recordingRoot = config?["Recording:Root"] ?? DefaultRecordingRoot;
            _ffmpegExe = config?["Recording:FfmpegPath"] ?? "ffmpeg";

            Directory.CreateDirectory(_recordingRoot);
        }

        // ==========================================
        // API CHÍNH
        // ==========================================
        public string StartRecording(string rtspUrl, string qrCode, string stationName, string username)
        {
            if (string.IsNullOrWhiteSpace(rtspUrl))
                throw new ArgumentException("rtspUrl is required");
            if (string.IsNullOrWhiteSpace(qrCode))
                throw new ArgumentException("qrCode is required");
            if (string.IsNullOrWhiteSpace(stationName))
                throw new ArgumentException("stationName is required");

            username = string.IsNullOrWhiteSpace(username) ? "UnknownUser" : username.Trim();
            qrCode = qrCode.Trim();
            stationName = stationName.Trim();

            // 1 trạm chỉ có 1 process -> stop cái cũ trước
            StopRecording(stationName);

            var safeQr = MakeSafe(qrCode);
            var safeUser = MakeSafe(username);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var folder = Path.Combine(_recordingRoot, stationName);
            Directory.CreateDirectory(folder);

            var fileName = $"{safeUser}_{safeQr}_{ts}.mp4";
            var fullPath = Path.Combine(folder, fileName);

            // ✅ Args tối ưu cho RTSP ổn định + ghi MP4 nhanh
            var args =
                "-hide_banner -loglevel error " +
                "-fflags +nobuffer -flags low_delay " +
                "-analyzeduration 2000000 -probesize 2000000 " +
                $"-rtsp_transport tcp -i \"{rtspUrl}\" " +
                "-an -c:v copy -movflags +faststart " +
                $"\"{fullPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            var p = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            p.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogWarning("[FFmpeg:{Station}] {Msg}", stationName, e.Data);
            };

            p.Exited += (_, __) =>
            {
                // Khi process tự chết (mạng rớt)
                _logger.LogWarning("[FFmpeg:{Station}] Process exited unexpectedly.", stationName);
            };

            if (!p.Start())
                throw new InvalidOperationException("Không thể khởi động FFmpeg");

            try
            {
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
            }
            catch { }

            _stationProcesses[stationName] = new StationProcess
            {
                Process = p,
                FileFullPath = fullPath,
                StartedAt = DateTime.Now,
                QrCode = qrCode,
                Username = username,
                RtspUrl = rtspUrl
            };

            // ✅ relative path khớp static files /videos
            var relative = Path.Combine("videos", stationName, fileName).Replace("\\", "/");
            return "/" + relative;
        }

        public void StopRecording(string stationName)
        {
            if (string.IsNullOrWhiteSpace(stationName)) return;

            if (_stationProcesses.TryRemove(stationName.Trim(), out var handle))
            {
                var p = handle.Process;

                try
                {
                    if (p.HasExited) return;

                    // ✅ graceful để MP4 finalize
                    try
                    {
                        if (p.StartInfo.RedirectStandardInput)
                        {
                            p.StandardInput.WriteLine("q");
                            p.StandardInput.Flush();
                        }
                    }
                    catch { }

                    // Chờ thoát mềm
                    if (!p.WaitForExit(1800))
                    {
                        try
                        {
                            p.Kill(true);
                            p.WaitForExit(2000);
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        // ==========================================
        // HELPERS TIỆN DỤNG
        // ==========================================
        public bool IsRecording(string stationName)
        {
            if (string.IsNullOrWhiteSpace(stationName)) return false;
            return _stationProcesses.ContainsKey(stationName.Trim());
        }

        public bool TryGetActiveFile(string stationName, out string fileFullPath)
        {
            fileFullPath = "";
            if (string.IsNullOrWhiteSpace(stationName)) return false;

            if (_stationProcesses.TryGetValue(stationName.Trim(), out var handle))
            {
                fileFullPath = handle.FileFullPath;
                return !string.IsNullOrWhiteSpace(fileFullPath);
            }
            return false;
        }

        public string? GetActiveFilePath(string stationName)
        {
            return TryGetActiveFile(stationName, out var p) ? p : null;
        }

        // ==========================================
        // SANITIZE
        // ==========================================
        private static string MakeSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return "NA";

            foreach (var c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');

            input = input.Replace("/", "_")
                         .Replace("\\", "_")
                         .Replace("..", "_");

            return input.Trim();
        }
    }
}
