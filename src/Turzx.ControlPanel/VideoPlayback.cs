using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Turzx.ControlPanel
{
    internal enum VideoOrientationMode
    {
        Auto,
        ScreenEncoded,
        Normal
    }

    internal sealed class VideoPlaybackRequest
    {
        public string FilePath { get; set; }
        public ImageFitMode FitMode { get; set; }
        public bool BackgroundWhite { get; set; }
        public int FramesPerSecond { get; set; }
        public VideoOrientationMode OrientationMode { get; set; }
        public bool Loop { get; set; }
        public RectangleF CustomCrop { get; set; }
        public int RotationDegrees { get; set; }
        public float CustomZoom { get; set; }
        public float CustomOffsetX { get; set; }
        public float CustomOffsetY { get; set; }
        public float CustomAspectRatio { get; set; }
    }

    internal sealed class VideoMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double DurationSeconds { get; set; }
        public double SourceFramesPerSecond { get; set; }

        public bool LooksScreenEncoded
        {
            get { return Height > Width * 2; }
        }
    }

    internal static class VideoDecoderLocator
    {
        public static string FindFfmpeg()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDirectory, "ffmpeg.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
            return null;
        }
    }

    internal static class VideoProbe
    {
        public static VideoMetadata Read(string ffmpegPath, string videoPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = "-hide_banner -nostdin -i " + Quote(videoPath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            string diagnostic;
            using (Process process = Process.Start(startInfo))
            {
                diagnostic = process.StandardError.ReadToEnd();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
            }

            Match size = Regex.Match(diagnostic,
                @",\s*(?<width>\d{2,5})x(?<height>\d{2,5})(?:\s|\[|,)",
                RegexOptions.IgnoreCase);
            if (!size.Success)
            {
                throw new InvalidOperationException("无法读取视频尺寸，请确认文件包含可播放的视频轨道。");
            }

            VideoMetadata metadata = new VideoMetadata();
            metadata.Width = int.Parse(size.Groups["width"].Value, CultureInfo.InvariantCulture);
            metadata.Height = int.Parse(size.Groups["height"].Value, CultureInfo.InvariantCulture);

            Match duration = Regex.Match(diagnostic,
                @"Duration:\s*(?<hours>\d+):(?<minutes>\d+):(?<seconds>\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase);
            if (duration.Success)
            {
                metadata.DurationSeconds =
                    int.Parse(duration.Groups["hours"].Value, CultureInfo.InvariantCulture) * 3600 +
                    int.Parse(duration.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60 +
                    double.Parse(duration.Groups["seconds"].Value, CultureInfo.InvariantCulture);
            }

            Match fps = Regex.Match(diagnostic, @"(?<fps>\d+(?:\.\d+)?)\s+fps",
                RegexOptions.IgnoreCase);
            if (fps.Success)
            {
                metadata.SourceFramesPerSecond = double.Parse(
                    fps.Groups["fps"].Value, CultureInfo.InvariantCulture);
            }
            return metadata;
        }

        internal static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class VideoFrameDecoder : IDisposable
    {
        private readonly VideoPlaybackRequest _request;
        private readonly VideoMetadata _metadata;
        private readonly string _ffmpegPath;
        private readonly bool _screenEncoded;
        private readonly int _outputWidth;
        private readonly int _outputHeight;
        private readonly int _frameByteCount;
        private readonly StringBuilder _diagnostic = new StringBuilder();
        private Process _process;

        public VideoFrameDecoder(string ffmpegPath, VideoPlaybackRequest request,
            VideoMetadata metadata)
        {
            _ffmpegPath = ffmpegPath;
            _request = request;
            _metadata = metadata;
            _screenEncoded = request.OrientationMode == VideoOrientationMode.ScreenEncoded ||
                (request.OrientationMode == VideoOrientationMode.Auto && metadata.LooksScreenEncoded);
            _outputWidth = ImageContentRenderer.LogicalWidth;
            _outputHeight = ImageContentRenderer.LogicalHeight;
            _frameByteCount = checked(_outputWidth * _outputHeight * 4);
            Start(false);
        }

        public bool IsScreenEncoded
        {
            get { return _screenEncoded; }
        }

        public byte[] ReadNextTransportPng(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] frame = new byte[_frameByteCount];
            int offset = 0;
            using (cancellationToken.Register(delegate { TryKill(); }))
            {
                while (offset < frame.Length)
                {
                    int read = _process.StandardOutput.BaseStream.Read(
                        frame, offset, frame.Length - offset);
                    if (read == 0)
                    {
                        break;
                    }
                    offset += read;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (offset == 0)
            {
                if (_request.Loop)
                {
                    throw new InvalidOperationException("视频解码意外结束。" + GetDiagnosticSuffix());
                }
                return null;
            }
            if (offset != frame.Length)
            {
                throw new InvalidOperationException("视频帧数据不完整。" + GetDiagnosticSuffix());
            }
            return EncodeTransportPng(frame, _outputWidth, _outputHeight, false);
        }

        public static byte[] ReadPreview(string ffmpegPath, VideoPlaybackRequest request,
            VideoMetadata metadata)
        {
            bool screenEncoded = request.OrientationMode == VideoOrientationMode.ScreenEncoded ||
                (request.OrientationMode == VideoOrientationMode.Auto && metadata.LooksScreenEncoded);
            int width = ImageContentRenderer.LogicalWidth;
            int height = ImageContentRenderer.LogicalHeight;
            int byteCount = checked(width * height * 4);
            string filter = BuildFilter(request, width, height, screenEncoded);
            double seek = metadata.DurationSeconds > 2.0 ? 1.0 : 0.0;

            ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath,
                "-ss " + seek.ToString("0.###", CultureInfo.InvariantCulture) +
                " -i " + VideoProbe.Quote(request.FilePath) +
                " -an -vf " + VideoProbe.Quote(filter) +
                " -frames:v 1 -f rawvideo -pix_fmt bgra pipe:1");

            using (Process process = Process.Start(startInfo))
            {
                byte[] frame = new byte[byteCount];
                int offset = 0;
                while (offset < byteCount)
                {
                    int read = process.StandardOutput.BaseStream.Read(
                        frame, offset, byteCount - offset);
                    if (read == 0)
                    {
                        break;
                    }
                    offset += read;
                }
                string diagnostic = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (offset != byteCount)
                {
                    throw new InvalidOperationException(
                        "无法解码视频预览。" + CompactDiagnostic(diagnostic));
                }
                return EncodeTransportPng(frame, width, height, false);
            }
        }

        public static Bitmap ReadSourcePreview(string ffmpegPath, string videoPath,
            VideoMetadata metadata, bool screenEncoded)
        {
            int byteCount = checked(metadata.Width * metadata.Height * 4);
            double seek = metadata.DurationSeconds > 2.0 ? 1.0 : 0.0;
            ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath,
                "-ss " + seek.ToString("0.###", CultureInfo.InvariantCulture) +
                " -i " + VideoProbe.Quote(videoPath) +
                " -an -frames:v 1 -f rawvideo -pix_fmt bgra pipe:1");
            using (Process process = Process.Start(startInfo))
            {
                byte[] frame = new byte[byteCount];
                int offset = 0;
                while (offset < byteCount)
                {
                    int read = process.StandardOutput.BaseStream.Read(
                        frame, offset, byteCount - offset);
                    if (read == 0)
                    {
                        break;
                    }
                    offset += read;
                }
                string diagnostic = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (offset != byteCount)
                {
                    throw new InvalidOperationException(
                        "无法解码视频裁剪预览。" + CompactDiagnostic(diagnostic));
                }
                Bitmap bitmap = CreateBitmapFromBgra(frame, metadata.Width, metadata.Height);
                if (screenEncoded)
                {
                    bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone);
                }
                return bitmap;
            }
        }

        private void Start(bool preview)
        {
            string loopArgument = _request.Loop ? "-stream_loop -1 " : string.Empty;
            string filter = BuildFilter(_request, _outputWidth, _outputHeight, _screenEncoded);
            string arguments = loopArgument + "-i " + VideoProbe.Quote(_request.FilePath) +
                " -an -vf " + VideoProbe.Quote(filter) +
                " -f rawvideo -pix_fmt bgra pipe:1";
            _process = new Process();
            _process.StartInfo = CreateStartInfo(_ffmpegPath, arguments);
            _process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (_diagnostic)
                    {
                        if (_diagnostic.Length < 2000)
                        {
                            _diagnostic.AppendLine(e.Data);
                        }
                    }
                }
            };
            _process.Start();
            _process.BeginErrorReadLine();
        }

        private static ProcessStartInfo CreateStartInfo(string ffmpegPath, string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = "-hide_banner -loglevel error -nostdin " + arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            return startInfo;
        }

        private static string BuildFilter(VideoPlaybackRequest request, int width, int height,
            bool screenEncoded)
        {
            int fps = Math.Max(1, Math.Min(30, request.FramesPerSecond));
            List<string> filters = new List<string>();
            filters.Add("fps=" + fps.ToString(CultureInfo.InvariantCulture));
            if (screenEncoded) filters.Add("transpose=2");
            switch (((request.RotationDegrees % 360) + 360) % 360)
            {
                case 90: filters.Add("transpose=1"); break;
                case 180: filters.Add("hflip"); filters.Add("vflip"); break;
                case 270: filters.Add("transpose=2"); break;
            }

            string scale;
            if (request.FitMode == ImageFitMode.Custom && request.CustomCrop.Width > 0 &&
                request.CustomCrop.Height > 0)
            {
                RectangleF selected = request.CustomCrop;
                filters.Add(string.Format(CultureInfo.InvariantCulture,
                    "crop=iw*{0:0.######}:ih*{1:0.######}:iw*{2:0.######}:ih*{3:0.######},",
                    selected.Width, selected.Height, selected.X, selected.Y).TrimEnd(','));
                filters.Add(string.Format(CultureInfo.InvariantCulture,
                    "scale={0}:{1}", width, height));
                return string.Join(",", filters.ToArray());
            }
            else if (request.FitMode == ImageFitMode.Custom)
            {
                float aspect = request.CustomAspectRatio > 0.01f ?
                    request.CustomAspectRatio : width / (float)height;
                float zoom = Math.Max(1.0f, Math.Min(8.0f, request.CustomZoom));
                float baseWidth;
                float baseHeight;
                if (aspect >= width / (float)height)
                {
                    baseWidth = width;
                    baseHeight = width / aspect;
                }
                else
                {
                    baseHeight = height;
                    baseWidth = height * aspect;
                }
                int scaledWidth = Math.Max(2, (int)Math.Round(baseWidth * zoom / 2.0f) * 2);
                int scaledHeight = Math.Max(2, (int)Math.Round(baseHeight * zoom / 2.0f) * 2);
                int cropWidth = Math.Min(width, scaledWidth);
                int cropHeight = Math.Min(height, scaledHeight);
                int cropX = scaledWidth > width ? Clamp((int)Math.Round(
                    (scaledWidth - width) / 2.0f - request.CustomOffsetX),
                    0, scaledWidth - width) : 0;
                int cropY = scaledHeight > height ? Clamp((int)Math.Round(
                    (scaledHeight - height) / 2.0f - request.CustomOffsetY),
                    0, scaledHeight - height) : 0;
                int padX = scaledWidth < width ? Clamp((int)Math.Round(
                    (width - scaledWidth) / 2.0f + request.CustomOffsetX),
                    0, width - scaledWidth) : 0;
                int padY = scaledHeight < height ? Clamp((int)Math.Round(
                    (height - scaledHeight) / 2.0f + request.CustomOffsetY),
                    0, height - scaledHeight) : 0;
                string color = request.BackgroundWhite ? "white" : "black";
                filters.Add(string.Format(CultureInfo.InvariantCulture,
                    "scale={0}:{1}", scaledWidth, scaledHeight));
                filters.Add(string.Format(CultureInfo.InvariantCulture,
                    "crop={0}:{1}:{2}:{3}", cropWidth, cropHeight, cropX, cropY));
                filters.Add(string.Format(CultureInfo.InvariantCulture,
                    "pad={0}:{1}:{2}:{3}:color={4}", width, height, padX, padY, color));
                return string.Join(",", filters.ToArray());
            }
            else if (request.FitMode == ImageFitMode.Stretch)
            {
                scale = string.Format(CultureInfo.InvariantCulture, "scale={0}:{1}", width, height);
            }
            else if (request.FitMode == ImageFitMode.Cover)
            {
                scale = string.Format(CultureInfo.InvariantCulture,
                    "scale={0}:{1}:force_original_aspect_ratio=increase,crop={0}:{1}",
                    width, height);
            }
            else
            {
                string color = request.BackgroundWhite ? "white" : "black";
                scale = string.Format(CultureInfo.InvariantCulture,
                    "scale={0}:{1}:force_original_aspect_ratio=decrease," +
                    "pad={0}:{1}:(ow-iw)/2:(oh-ih)/2:color={2}", width, height, color);
            }
            filters.Add(scale);
            return string.Join(",", filters.ToArray());
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static byte[] EncodeTransportPng(byte[] bgra, int width, int height,
            bool alreadyTransport)
        {
            using (Bitmap bitmap = CreateBitmapFromBgra(bgra, width, height))
            {
                if (!alreadyTransport)
                {
                    bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
                }
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private static Bitmap CreateBitmapFromBgra(byte[] bgra, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rectangle = new Rectangle(0, 0, width, height);
            BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int sourceStride = width * 4;
                for (int row = 0; row < height; row++)
                {
                    IntPtr destination = IntPtr.Add(data.Scan0, row * data.Stride);
                    Marshal.Copy(bgra, row * sourceStride, destination, sourceStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private string GetDiagnosticSuffix()
        {
            lock (_diagnostic)
            {
                return CompactDiagnostic(_diagnostic.ToString());
            }
        }

        private static string CompactDiagnostic(string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                return string.Empty;
            }
            string value = diagnostic.Trim().Replace("\r", " ").Replace("\n", " ");
            return " 解码器信息：" + (value.Length > 300 ? value.Substring(0, 300) : value);
        }

        private void TryKill()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            TryKill();
            if (_process != null)
            {
                try
                {
                    _process.WaitForExit(1000);
                }
                catch
                {
                }
                _process.Dispose();
                _process = null;
            }
        }
    }
}
