using System.Diagnostics;
using System.Collections.Concurrent;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        private static readonly ConcurrentDictionary<string, Process> Active = new();
        private readonly string Root = @"C:\GhiHinhVideos";

        public FfmpegService()
        {
            if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
        }

        public string StartRecording(string rtsp, string qr, string station, string user)
        {
            string key = station.ToLower();

            if (IsStop(qr))
            {
                StopRecording(station);
                return "STOPPED";
            }

            if (Active.ContainsKey(key)) StopRecording(station);

            string dir = Path.Combine(Root, station);
            Directory.CreateDirectory(dir);

            string safeQr = qr.Replace("/", "_").Replace("\\", "_");
            string safeUser = user.Replace(" ", "_");
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string file = $"{safeUser}_{safeQr}_{ts}.mp4";
            string output = Path.Combine(dir, file);

            string ff = FindFfmpeg();
            if (ff == null) throw new FileNotFoundException("Không tìm thấy ffmpeg.exe");

            string overlay = "drawtext=text='%{localtime\\:%Y-%m-%d %H\\\\\\:%M\\\\\\:%S}':"
                           + "fontcolor=white:fontsize=36:box=1:boxcolor=black@0.5:x=w-tw-20:y=20";

            string args =
                $"-rtsp_transport tcp -i \"{rtsp}\" " +
                $"-vf \"{overlay}\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -an " +
                $"\"{output}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ff,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi };
            proc.Start();

            Active[key] = proc;
            return output;
        }

        public bool StopRecording(string station)
        {
            string key = station.ToLower();
            if (!Active.TryRemove(key, out var proc)) return false;

            try
            {
                if (!proc.HasExited)
                {
                    try
                    {
                        using var sw = proc.StandardInput;
                        sw.WriteLine("q");
                    }
                    catch { }

                    if (!proc.WaitForExit(1500))
                        proc.Kill();
                }
                return true;
            }
            catch
            {
                try { proc.Kill(); } catch { }
                return false;
            }
        }

        private bool IsStop(string c) =>
            c.Equals("STOP", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("STOP RECORDING", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("@@STOP_RECORD@@", StringComparison.OrdinalIgnoreCase);

        private string? FindFfmpeg()
        {
            string p1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string p2 = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
            if (File.Exists(p1)) return p1;
            if (File.Exists(p2)) return p2;
            return null;
        }
    }
}
