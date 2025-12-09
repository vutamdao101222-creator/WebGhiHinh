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
using Microsoft.Extensions.Logging;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        private sealed class StationProcess
        {
            public Process Process { get; init; } = default!;
            public string FileFullPath { get; init; } = "";
        }

        private readonly ConcurrentDictionary<string, StationProcess> _stationProcesses = new();
        private readonly ILogger<FfmpegService> _logger;

        // ✅ Root bạn muốn
        private const string RecordingRoot = @"C:\GhiHinhVideos";
        private readonly string _ffmpegExe = "ffmpeg";

        public FfmpegService(ILogger<FfmpegService> logger)
        {
            _logger = logger;
            Directory.CreateDirectory(RecordingRoot);
        }

        public string StartRecording(string rtspUrl, string qrCode, string stationName, string username)
        {
            if (string.IsNullOrWhiteSpace(rtspUrl)) throw new ArgumentException("rtspUrl is required");
            if (string.IsNullOrWhiteSpace(qrCode)) throw new ArgumentException("qrCode is required");
            if (string.IsNullOrWhiteSpace(stationName)) throw new ArgumentException("stationName is required");
            if (string.IsNullOrWhiteSpace(username)) username = "UnknownUser";

            StopRecording(stationName);

            var safeQr = MakeSafe(qrCode);
            var safeUser = MakeSafe(username);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var folder = Path.Combine(RecordingRoot, stationName);
            Directory.CreateDirectory(folder);

            // ✅ format giống bạn: gigahoang_XKHL..._20251204_100822
            var fileName = $"{safeUser}_{safeQr}_{ts}.mp4";
            var fullPath = Path.Combine(folder, fileName);

            var args =
                "-hide_banner -loglevel error " +
                "-fflags +nobuffer -flags low_delay " +
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

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogWarning("[FFmpeg:{Station}] {Msg}", stationName, e.Data);
            };

            if (!p.Start())
                throw new InvalidOperationException("Không thể khởi động FFmpeg");

            try { p.BeginErrorReadLine(); p.BeginOutputReadLine(); } catch { }

            _stationProcesses[stationName] = new StationProcess
            {
                Process = p,
                FileFullPath = fullPath
            };

            // ✅ relative path khớp static files /videos
            // Program.cs:
            // var videoPath = @"C:\GhiHinhVideos";
            // app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(videoPath), RequestPath = "/videos" })
            var relative = Path.Combine("videos", stationName, fileName).Replace("\\", "/");
            return "/" + relative;
        }

        public void StopRecording(string stationName)
        {
            if (string.IsNullOrWhiteSpace(stationName)) return;

            if (_stationProcesses.TryRemove(stationName, out var handle))
            {
                var p = handle.Process;

                try
                {
                    if (p.HasExited) return;

                    // ✅ graceful để MP4 finalize
                    try
                    {
                        p.StandardInput.WriteLine("q");
                        p.StandardInput.Flush();
                    }
                    catch { }

                    if (!p.WaitForExit(1500))
                    {
                        try { p.Kill(true); p.WaitForExit(2000); } catch { }
                    }
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        private static string MakeSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return "NA";
            foreach (var c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');

            input = input.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
            return input.Trim();
        }
    }
}
