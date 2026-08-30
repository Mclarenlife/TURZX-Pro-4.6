using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Turzx.ControlPanel;

namespace Turzx.ProtocolProbe
{
    internal static class Program
    {
        private const ushort VendorId = 0x1CBE;
        private const ushort ProductId = 0x0046;

        private static int Main(string[] args)
        {
            string command = args.Length == 0 ? "probe" : args[0].ToLowerInvariant();
            bool pauseBeforeExit = args.Length == 0 && !Console.IsInputRedirected;

            try
            {
                Console.WriteLine("TURZX 4.6-inch WinUSB protocol probe");
                Console.WriteLine("Target: VID {0:X4}, PID {1:X4}", VendorId, ProductId);

                if (command == "generate-test-image")
                {
                    return GenerateTestImage();
                }

                if (command == "telemetry")
                {
                    return RunTelemetryTest();
                }

                if (command == "sensors")
                {
                    return RunSensorTest();
                }

                if ((command == "test-image" || command == "motion-test" ||
                    command == "dashboard-test" || command == "video-test") &&
                    (args.Length < 2 || args[1] != "--confirm-display-write"))
                {
                    Console.Error.WriteLine("SAFETY LOCK: this command can change or disrupt the current display.");
                    Console.Error.WriteLine("Generate and inspect it offline first, then use:");
                    Console.Error.WriteLine("  Turzx.ProtocolProbe.exe test-image --confirm-display-write");
                    Console.Error.WriteLine("  Turzx.ProtocolProbe.exe motion-test --confirm-display-write");
                    Console.Error.WriteLine("  Turzx.ProtocolProbe.exe dashboard-test --confirm-display-write");
                    Console.Error.WriteLine("  Turzx.ProtocolProbe.exe video-test --confirm-display-write <mp4> [frames] [fps] [custom]");
                    return 3;
                }

                using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                {
                    PrintDevice(device);

                    if (command == "probe")
                    {
                        Console.WriteLine("Probe completed. No data was sent.");
                        return 0;
                    }

                    if (command == "sync")
                    {
                        byte[] response = device.Exchange(TurzxProtocol.CreateCommand(10), 10);
                        PrintResponse("SYNC", response);
                        return 0;
                    }

                    if (command == "test-image")
                    {
                        return SendTestImage(device);
                    }

                    if (command == "motion-test")
                    {
                        return SendMotionTest(device);
                    }

                    if (command == "dashboard-test")
                    {
                        return SendDashboardTest(device);
                    }

                    if (command == "video-test")
                    {
                        return SendVideoTest(device, args);
                    }

                    Console.Error.WriteLine("Unknown command: {0}", command);
                    PrintUsage();
                    return 2;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("ERROR: {0}", exception.Message);
                return 1;
            }
            finally
            {
                if (pauseBeforeExit)
                {
                    Console.WriteLine();
                    Console.Write("Press any key to close...");
                    Console.ReadKey(true);
                }
            }
        }

        private static int GenerateTestImage()
        {
            string outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string imagePath = Path.Combine(outputDirectory, "test-pattern.png");
            byte[] png = TestPattern.CreateLandscapePng(0, 1);
            string description = TurzxProtocol.DescribePanelPng(png);
            File.WriteAllBytes(imagePath, png);
            Console.WriteLine("Generated only; no USB device was opened.");
            Console.WriteLine("Test image: {0} ({1:N0} bytes)", imagePath, png.Length);
            Console.WriteLine("Validated: {0}", description);
            return 0;
        }

        private static int RunTelemetryTest()
        {
            Console.WriteLine("Sampling Windows telemetry for one second; no USB device will be opened.");
            SystemTelemetry telemetry = new SystemTelemetry();
            telemetry.Capture();
            Thread.Sleep(1000);
            TelemetrySnapshot sample = telemetry.Capture();
            using (HardwareSensorProvider sensors = new HardwareSensorProvider())
            {
                sensors.Enrich(sample);
            }
            byte[] png = DashboardRenderer.CreateTransportPng(
                sample, System.Drawing.Color.FromArgb(46, 230, 166));
            string description = TurzxProtocol.DescribePanelPng(png);
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dashboard-preview.png");
            File.WriteAllBytes(outputPath, png);

            Console.WriteLine("CPU:     {0:0.0}%", sample.CpuPercent);
            Console.WriteLine("Memory:  {0:0.0}%", sample.MemoryPercent);
            Console.WriteLine("Disk:    {0:0.0}%", sample.DiskPercent);
            Console.WriteLine("Network: down {0:0.00} Mb/s, up {1:0.00} Mb/s",
                sample.NetworkDownloadMbps, sample.NetworkUploadMbps);
            Console.WriteLine("CPU name: {0}", sample.CpuName);
            Console.WriteLine("GPU name: {0}", sample.GpuName);
            PrintSensors(sample);
            Console.WriteLine("Dashboard: {0}", outputPath);
            Console.WriteLine("Validated: {0}", description);
            return 0;
        }

        private static int RunSensorTest()
        {
            Console.WriteLine("Reading hardware sensors; no USB device will be opened.");
            SystemTelemetry telemetry = new SystemTelemetry();
            TelemetrySnapshot sample = telemetry.Capture();
            using (HardwareSensorProvider sensors = new HardwareSensorProvider())
            {
                sensors.Enrich(sample);
                if (!string.IsNullOrWhiteSpace(sensors.InitializationError))
                {
                    Console.WriteLine("LibreHardwareMonitor note: {0}", sensors.InitializationError);
                }
            }
            PrintSensors(sample);
            return 0;
        }

        private static int SendMotionTest(WinUsbDevice device)
        {
            const int frameCount = 12;
            Console.WriteLine("Starting a {0}-frame landscape motion test...", frameCount);

            byte[] syncResponse = device.Exchange(TurzxProtocol.CreateCommand(10), 10);
            PrintResponse("SYNC", syncResponse);

            for (int frame = 0; frame < frameCount; frame++)
            {
                byte[] png = TestPattern.CreateLandscapePng(frame, frameCount);
                TurzxProtocol.DescribePanelPng(png);
                byte[] response = device.Exchange(TurzxProtocol.CreateImageUpload(png), 102);
                bool success = response.Length > 1 && response[1] == 0xC8;
                Console.WriteLine("Frame {0,2}/{1}: {2:N0} bytes, response {3}",
                    frame + 1, frameCount, png.Length, success ? "OK" : "UNEXPECTED");
                if (!success)
                {
                    return 4;
                }
                Thread.Sleep(750);
            }

            Console.WriteLine("Motion test completed. The frame number and progress bar should have moved.");
            return 0;
        }

        private static int SendDashboardTest(WinUsbDevice device)
        {
            const int frameCount = 10;
            SystemTelemetry telemetry = new SystemTelemetry();
            telemetry.Capture();
            Thread.Sleep(500);
            PrintResponse("SYNC", device.Exchange(TurzxProtocol.CreateCommand(10), 10));
            Console.WriteLine("Sending {0} live telemetry frames...", frameCount);

            using (HardwareSensorProvider sensors = new HardwareSensorProvider())
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    TelemetrySnapshot sample = telemetry.Capture();
                    sensors.Enrich(sample);
                    byte[] png = DashboardRenderer.CreateTransportPng(
                        sample, System.Drawing.Color.FromArgb(46, 230, 166));
                    TurzxProtocol.DescribePanelPng(png);
                    byte[] response = device.Exchange(TurzxProtocol.CreateImageUpload(png), 102);
                    bool success = response.Length > 1 && response[1] == 0xC8;
                    Console.WriteLine(
                        "Frame {0,2}/{1}: CPU {2,5:0.0}% {3,5}  GPU {4,5:0}% {5,5}  {6}",
                        frame + 1, frameCount, sample.CpuPercent,
                        FormatNullable(sample.CpuTemperatureC, "0", "C"),
                        sample.GpuLoadPercent ?? 0,
                        FormatNullable(sample.GpuTemperatureC, "0", "C"),
                        success ? "OK" : "UNEXPECTED");
                    if (!success)
                    {
                        return 4;
                    }
                    Thread.Sleep(1000);
                }
            }

            Console.WriteLine("Live dashboard test completed.");
            return 0;
        }

        private static int SendVideoTest(WinUsbDevice device, string[] args)
        {
            if (args.Length < 3 || !File.Exists(args[2]))
            {
                Console.Error.WriteLine("Video file is missing or does not exist.");
                Console.Error.WriteLine(
                    "Usage: Turzx.ProtocolProbe.exe video-test --confirm-display-write <mp4> [frames] [fps] [custom]");
                return 2;
            }

            int frameLimit = 20;
            if (args.Length >= 4 && !int.TryParse(args[3], out frameLimit))
            {
                frameLimit = 20;
            }
            frameLimit = Math.Max(1, Math.Min(300, frameLimit));
            int outputFps = 5;
            if (args.Length >= 5 && !int.TryParse(args[4], out outputFps))
            {
                outputFps = 5;
            }
            outputFps = Math.Max(1, Math.Min(30, outputFps));
            bool customCrop = args.Length >= 6 && string.Equals(
                args[5], "custom", StringComparison.OrdinalIgnoreCase);
            string ffmpegPath = VideoDecoderLocator.FindFfmpeg();
            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                Console.Error.WriteLine("FFmpeg decoder was not found.");
                return 1;
            }

            VideoPlaybackRequest request = new VideoPlaybackRequest
            {
                FilePath = args[2],
                FitMode = customCrop ? ImageFitMode.Custom : ImageFitMode.Cover,
                BackgroundWhite = false,
                FramesPerSecond = outputFps,
                OrientationMode = VideoOrientationMode.Auto,
                Loop = false,
                CustomCrop = customCrop ? new System.Drawing.RectangleF(
                    0.125f, 0, 0.75f, 1.0f) : System.Drawing.RectangleF.Empty
            };
            VideoMetadata metadata = VideoProbe.Read(ffmpegPath, request.FilePath);
            Console.WriteLine("Video: {0}x{1}, {2:0.##} sec, {3:0.##} fps",
                metadata.Width, metadata.Height, metadata.DurationSeconds,
                metadata.SourceFramesPerSecond);
            Console.WriteLine("Orientation: {0}",
                metadata.LooksScreenEncoded ? "screen-encoded" : "normal");
            PrintResponse("SYNC", device.Exchange(TurzxProtocol.CreateCommand(10), 10));

            Stopwatch total = Stopwatch.StartNew();
            using (VideoFrameDecoder decoder = new VideoFrameDecoder(
                ffmpegPath, request, metadata))
            {
                for (int frameNumber = 1; frameNumber <= frameLimit; frameNumber++)
                {
                    Stopwatch frameTimer = Stopwatch.StartNew();
                    byte[] png = decoder.ReadNextTransportPng(CancellationToken.None);
                    if (png == null)
                    {
                        break;
                    }
                    if (frameNumber == 1)
                    {
                        Console.WriteLine("Validated: {0}", TurzxProtocol.DescribePanelPng(png));
                        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "video-preview.png"), png);
                    }
                    byte[] response = device.Exchange(
                        TurzxProtocol.CreateImageUpload(png), 102);
                    bool success = response.Length > 1 && response[1] == 0xC8;
                    Console.WriteLine("Frame {0,3}/{1}: {2,8:N0} bytes, {3,4} ms, {4}",
                        frameNumber, frameLimit, png.Length, frameTimer.ElapsedMilliseconds,
                        success ? "OK" : "UNEXPECTED");
                    if (!success)
                    {
                        return 4;
                    }
                    int remaining = (1000 / outputFps) - (int)frameTimer.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        Thread.Sleep(remaining);
                    }
                }
            }
            Console.WriteLine("Video test completed in {0:0.0} seconds.", total.Elapsed.TotalSeconds);
            return 0;
        }

        private static void PrintSensors(TelemetrySnapshot sample)
        {
            Console.WriteLine("Sensor source: {0}", sample.SensorSource ?? "Unavailable");
            Console.WriteLine("CPU sensors: {0}, {1}, fan {2}",
                FormatNullable(sample.CpuTemperatureC, "0.0", " C"),
                FormatNullable(sample.CpuPowerWatts, "0.0", " W"),
                FormatNullable(sample.CpuFanRpm, "0", " RPM"));
            Console.WriteLine("GPU sensors: load {0}, {1}, {2}, fan {3}",
                FormatNullable(sample.GpuLoadPercent, "0.0", "%"),
                FormatNullable(sample.GpuTemperatureC, "0.0", " C"),
                FormatNullable(sample.GpuPowerWatts, "0.0", " W"),
                sample.GpuFanRpm.HasValue ?
                    FormatNullable(sample.GpuFanRpm, "0", " RPM") :
                    FormatNullable(sample.GpuFanPercent, "0", "%"));
            Console.WriteLine("GPU memory: {0} / {1}",
                FormatNullable(sample.GpuMemoryUsedMb, "0", " MB"),
                FormatNullable(sample.GpuMemoryTotalMb, "0", " MB"));
        }

        private static string FormatNullable(double? value, string format, string unit)
        {
            return value.HasValue ? value.Value.ToString(format) + unit : "--";
        }

        private static int SendTestImage(WinUsbDevice device)
        {
            string outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string imagePath = Path.Combine(outputDirectory, "test-pattern.png");
            byte[] png = TestPattern.CreateLandscapePng(0, 1);
            string description = TurzxProtocol.DescribePanelPng(png);
            File.WriteAllBytes(imagePath, png);

            Console.WriteLine("Test image: {0} ({1:N0} bytes)", imagePath, png.Length);
            Console.WriteLine("Validated: {0}", description);

            byte[] syncResponse = device.Exchange(TurzxProtocol.CreateCommand(10), 10);
            PrintResponse("SYNC", syncResponse);

            byte[] imageResponse = device.Exchange(TurzxProtocol.CreateImageUpload(png), 102);
            PrintResponse("UPLOAD_PNG", imageResponse);

            Console.WriteLine("Test image sent. The operation did not update firmware.");
            return 0;
        }

        private static void PrintDevice(WinUsbDevice device)
        {
            Console.WriteLine("Device path: {0}", device.DevicePath);
            Console.WriteLine("Interface: class {0:X2}, subclass {1:X2}, protocol {2:X2}",
                device.InterfaceClass, device.InterfaceSubClass, device.InterfaceProtocol);
            Console.WriteLine("OUT endpoint: 0x{0:X2} ({1}, max packet {2})",
                device.OutEndpoint, device.OutPipeType, device.OutMaxPacketSize);
            Console.WriteLine("IN endpoint:  0x{0:X2} ({1}, max packet {2})",
                device.InEndpoint, device.InPipeType, device.InMaxPacketSize);
        }

        private static void PrintResponse(string operation, byte[] response)
        {
            int count = Math.Min(response.Length, 32);
            string[] bytes = new string[count];
            for (int index = 0; index < count; index++)
            {
                bytes[index] = response[index].ToString("X2");
            }

            Console.WriteLine("{0} response: {1} bytes", operation, response.Length);
            Console.WriteLine("  {0}", string.Join(" ", bytes));

            if (response.Length > 8)
            {
                Console.WriteLine("  status candidates: byte[1]=0x{0:X2}, byte[8]=0x{1:X2}",
                    response[1], response[8]);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Turzx.ProtocolProbe.exe probe");
            Console.WriteLine("  Turzx.ProtocolProbe.exe sync");
            Console.WriteLine("  Turzx.ProtocolProbe.exe telemetry");
            Console.WriteLine("  Turzx.ProtocolProbe.exe sensors");
            Console.WriteLine("  Turzx.ProtocolProbe.exe generate-test-image");
            Console.WriteLine("  Turzx.ProtocolProbe.exe test-image --confirm-display-write");
            Console.WriteLine("  Turzx.ProtocolProbe.exe motion-test --confirm-display-write");
            Console.WriteLine("  Turzx.ProtocolProbe.exe dashboard-test --confirm-display-write");
            Console.WriteLine("  Turzx.ProtocolProbe.exe video-test --confirm-display-write <mp4> [frames] [fps] [custom]");
        }
    }
}
