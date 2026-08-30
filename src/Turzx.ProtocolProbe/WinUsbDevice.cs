using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Turzx.ProtocolProbe
{
    internal sealed class WinUsbDevice : IDisposable
    {
        private static readonly Guid UsbDeviceInterfaceGuid =
            new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        private SafeFileHandle _fileHandle;
        private IntPtr _winUsbHandle;

        private WinUsbDevice()
        {
        }

        public string DevicePath { get; private set; }
        public byte InterfaceClass { get; private set; }
        public byte InterfaceSubClass { get; private set; }
        public byte InterfaceProtocol { get; private set; }
        public byte OutEndpoint { get; private set; }
        public byte InEndpoint { get; private set; }
        public ushort OutMaxPacketSize { get; private set; }
        public ushort InMaxPacketSize { get; private set; }
        public UsbPipeType OutPipeType { get; private set; }
        public UsbPipeType InPipeType { get; private set; }

        public static WinUsbDevice Open(ushort vendorId, ushort productId)
        {
            string marker = string.Format("vid_{0:x4}&pid_{1:x4}", vendorId, productId);
            foreach (string path in EnumerateUsbDevicePaths())
            {
                if (path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                WinUsbDevice device = new WinUsbDevice();
                try
                {
                    device.OpenPath(path);
                    return device;
                }
                catch
                {
                    device.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException(string.Format(
                "TURZX device {0:X4}:{1:X4} was not found.", vendorId, productId));
        }

        public byte[] Exchange(byte[] payload, byte expectedResponseCommand)
        {
            if (payload == null)
            {
                throw new ArgumentNullException("payload");
            }

            // A previous process can leave a late response queued. Discard it so the
            // response returned below always belongs to this request.
            NativeMethods.WinUsb_FlushPipe(_winUsbHandle, InEndpoint);

            uint transferred;
            if (!NativeMethods.WinUsb_WritePipe(_winUsbHandle, OutEndpoint, payload,
                (uint)payload.Length, out transferred, IntPtr.Zero))
            {
                throw NewWin32Exception("WinUSB write failed");
            }

            if (transferred != payload.Length)
            {
                throw new InvalidOperationException(string.Format(
                    "WinUSB write was incomplete: {0} of {1} bytes.", transferred, payload.Length));
            }

            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 2500)
            {
                byte[] buffer = new byte[512];
                if (!NativeMethods.WinUsb_ReadPipe(_winUsbHandle, InEndpoint, buffer,
                    (uint)buffer.Length, out transferred, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorSemTimeout)
                    {
                        break;
                    }
                    throw new Win32Exception(error, "WinUSB read failed (" + error + ")");
                }

                // This device can emit a zero-length packet before its 512-byte
                // command response. It is transport framing, not the response.
                if (transferred == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                byte[] response = new byte[transferred];
                Buffer.BlockCopy(buffer, 0, response, 0, (int)transferred);
                if (response[0] == expectedResponseCommand)
                {
                    return response;
                }

                Console.WriteLine(
                    "Discarded stale response for command 0x{0:X2}; waiting for 0x{1:X2}.",
                    response[0], expectedResponseCommand);
            }

            throw new TimeoutException("The TURZX device did not return a command response within 2.5 seconds.");
        }

        public void Dispose()
        {
            if (_winUsbHandle != IntPtr.Zero)
            {
                NativeMethods.WinUsb_Free(_winUsbHandle);
                _winUsbHandle = IntPtr.Zero;
            }

            if (_fileHandle != null)
            {
                _fileHandle.Dispose();
                _fileHandle = null;
            }
        }

        private void OpenPath(string path)
        {
            DevicePath = path;
            _fileHandle = NativeMethods.CreateFile(path,
                NativeMethods.GenericRead | NativeMethods.GenericWrite,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                IntPtr.Zero, NativeMethods.OpenExisting,
                NativeMethods.FileAttributeNormal | NativeMethods.FileFlagOverlapped,
                IntPtr.Zero);

            if (_fileHandle.IsInvalid)
            {
                throw NewWin32Exception("Unable to open the TURZX WinUSB device");
            }

            if (!NativeMethods.WinUsb_Initialize(_fileHandle, out _winUsbHandle))
            {
                throw NewWin32Exception("WinUsb_Initialize failed");
            }

            UsbInterfaceDescriptor descriptor;
            if (!NativeMethods.WinUsb_QueryInterfaceSettings(_winUsbHandle, 0, out descriptor))
            {
                throw NewWin32Exception("WinUsb_QueryInterfaceSettings failed");
            }

            InterfaceClass = descriptor.InterfaceClass;
            InterfaceSubClass = descriptor.InterfaceSubClass;
            InterfaceProtocol = descriptor.InterfaceProtocol;

            for (byte index = 0; index < descriptor.NumEndpoints; index++)
            {
                WinUsbPipeInformation pipe;
                if (!NativeMethods.WinUsb_QueryPipe(_winUsbHandle, 0, index, out pipe))
                {
                    throw NewWin32Exception("WinUsb_QueryPipe failed");
                }

                if ((pipe.PipeId & 0x80) == 0)
                {
                    OutEndpoint = pipe.PipeId;
                    OutMaxPacketSize = pipe.MaximumPacketSize;
                    OutPipeType = pipe.PipeType;
                }
                else
                {
                    InEndpoint = pipe.PipeId;
                    InMaxPacketSize = pipe.MaximumPacketSize;
                    InPipeType = pipe.PipeType;
                }
            }

            if (OutEndpoint == 0 || InEndpoint == 0)
            {
                throw new InvalidOperationException("The TURZX bulk IN/OUT endpoints were not found.");
            }

            SetTransferTimeout(OutEndpoint, 2000);
            SetTransferTimeout(InEndpoint, 2000);
        }

        private void SetTransferTimeout(byte pipeId, uint timeoutMilliseconds)
        {
            uint valueLength = sizeof(uint);
            if (!NativeMethods.WinUsb_SetPipePolicy(_winUsbHandle, pipeId,
                NativeMethods.PipeTransferTimeout, valueLength, ref timeoutMilliseconds))
            {
                throw NewWin32Exception("Unable to set WinUSB pipe timeout");
            }
        }

        private static IEnumerable<string> EnumerateUsbDevicePaths()
        {
            Guid interfaceGuid = UsbDeviceInterfaceGuid;
            IntPtr infoSet = NativeMethods.SetupDiGetClassDevs(ref interfaceGuid,
                null, IntPtr.Zero, NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
            if (infoSet == NativeMethods.InvalidHandleValue)
            {
                throw NewWin32Exception("SetupDiGetClassDevs failed");
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    DeviceInterfaceData interfaceData = new DeviceInterfaceData();
                    interfaceData.Size = Marshal.SizeOf(typeof(DeviceInterfaceData));
                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero,
                        ref interfaceGuid, index, ref interfaceData))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == NativeMethods.ErrorNoMoreItems)
                        {
                            yield break;
                        }
                        throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed");
                    }

                    uint requiredSize = 0;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData,
                        IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet,
                            ref interfaceData, detailBuffer, requiredSize,
                            ref requiredSize, IntPtr.Zero))
                        {
                            throw NewWin32Exception("SetupDiGetDeviceInterfaceDetail failed");
                        }

                        string path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                        if (!string.IsNullOrEmpty(path))
                        {
                            yield return path;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }

                    index++;
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(infoSet);
            }
        }

        private static Win32Exception NewWin32Exception(string message)
        {
            int error = Marshal.GetLastWin32Error();
            return new Win32Exception(error, message + " (" + error + ")");
        }

        internal enum UsbPipeType
        {
            Control = 0,
            Isochronous = 1,
            Bulk = 2,
            Interrupt = 3
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UsbInterfaceDescriptor
        {
            public byte Length;
            public byte DescriptorType;
            public byte InterfaceNumber;
            public byte AlternateSetting;
            public byte NumEndpoints;
            public byte InterfaceClass;
            public byte InterfaceSubClass;
            public byte InterfaceProtocol;
            public byte Interface;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinUsbPipeInformation
        {
            public UsbPipeType PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }

        private static class NativeMethods
        {
            internal const uint DigcfPresent = 0x00000002;
            internal const uint DigcfDeviceInterface = 0x00000010;
            internal const int ErrorNoMoreItems = 259;
            internal const int ErrorSemTimeout = 121;
            internal const uint GenericRead = 0x80000000;
            internal const uint GenericWrite = 0x40000000;
            internal const uint FileShareRead = 0x00000001;
            internal const uint FileShareWrite = 0x00000002;
            internal const uint OpenExisting = 3;
            internal const uint FileAttributeNormal = 0x00000080;
            internal const uint FileFlagOverlapped = 0x40000000;
            internal const uint PipeTransferTimeout = 0x03;
            internal static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid,
                string enumerator, IntPtr parentWindow, uint flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
                IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex,
                ref DeviceInterfaceData deviceInterfaceData);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
                ref DeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
                uint deviceInterfaceDetailDataSize, ref uint requiredSize, IntPtr deviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
                uint shareMode, IntPtr securityAttributes, uint creationDisposition,
                uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle,
                out IntPtr interfaceHandle);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_Free(IntPtr interfaceHandle);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle,
                byte alternateInterfaceNumber, out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle,
                byte alternateInterfaceNumber, byte pipeIndex,
                out WinUsbPipeInformation pipeInformation);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle,
                byte pipeId, uint policyType, uint valueLength, ref uint value);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId,
                byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId,
                byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

            [DllImport("winusb.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool WinUsb_FlushPipe(IntPtr interfaceHandle, byte pipeId);
        }
    }
}
