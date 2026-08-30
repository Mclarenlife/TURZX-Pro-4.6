using System;
using System.Security.Cryptography;
using System.Text;

namespace Turzx.ProtocolProbe
{
    internal static class TurzxProtocol
    {
        private const int PlainCommandLength = 500;
        private const int PacketLength = 512;
        private const byte UploadPngCommand = 102;
        private static readonly byte[] CipherKey = Encoding.ASCII.GetBytes("slv3tuzx");

        public static byte[] CreateCommand(byte command)
        {
            return EncryptCommand(CreatePlainCommand(command));
        }

        public static byte[] CreateBrightnessCommand(int percent)
        {
            if (percent < 0 || percent > 100)
            {
                throw new ArgumentOutOfRangeException("percent", "Brightness must be between 0 and 100.");
            }

            byte[] plain = CreatePlainCommand(14);
            plain[8] = (byte)Math.Round(percent / 100.0 * 102.0);
            return EncryptCommand(plain);
        }

        public static byte[] CreateImageUpload(byte[] png)
        {
            if (png == null)
            {
                throw new ArgumentNullException("png");
            }

            if (png.Length == 0 || png.Length > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException("png", "PNG payload must be between 1 byte and 1 MiB.");
            }

            ValidatePanelPng(png);

            byte[] plain = CreatePlainCommand(UploadPngCommand);
            WriteUInt32BigEndian(plain, 8, (uint)png.Length);

            byte[] header = EncryptCommand(plain);
            byte[] payload = new byte[header.Length + png.Length];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            Buffer.BlockCopy(png, 0, payload, header.Length, png.Length);
            return payload;
        }

        public static string DescribePanelPng(byte[] png)
        {
            ValidatePanelPng(png);
            return string.Format("{0}x{1}, bit depth {2}, RGBA color type {3}",
                ReadUInt32BigEndian(png, 16), ReadUInt32BigEndian(png, 20), png[24], png[25]);
        }

        private static byte[] CreatePlainCommand(byte command)
        {
            byte[] packet = new byte[PlainCommandLength];
            packet[0] = command;
            packet[2] = 0x1A;
            packet[3] = 0x6D;

            uint millisecondsSinceMidnight = (uint)DateTime.Now.TimeOfDay.TotalMilliseconds;
            byte[] timestamp = BitConverter.GetBytes(millisecondsSinceMidnight);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(timestamp);
            }
            Buffer.BlockCopy(timestamp, 0, packet, 4, timestamp.Length);
            return packet;
        }

        private static byte[] EncryptCommand(byte[] plain)
        {
            byte[] encrypted;
            using (DES des = DES.Create())
            {
                des.Key = CipherKey;
                des.IV = CipherKey;
                des.Mode = CipherMode.CBC;
                des.Padding = PaddingMode.Zeros;
                using (ICryptoTransform encryptor = des.CreateEncryptor())
                {
                    encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }

            if (encrypted.Length > PacketLength - 2)
            {
                throw new InvalidOperationException("Encrypted command is larger than the protocol packet.");
            }

            byte[] packet = new byte[PacketLength];
            Buffer.BlockCopy(encrypted, 0, packet, 0, encrypted.Length);
            packet[510] = 0xA1;
            packet[511] = 0x1A;
            return packet;
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void ValidatePanelPng(byte[] png)
        {
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (png.Length < 29)
            {
                throw new ArgumentException("PNG is too short.", "png");
            }

            for (int index = 0; index < signature.Length; index++)
            {
                if (png[index] != signature[index])
                {
                    throw new ArgumentException("Image does not have a valid PNG signature.", "png");
                }
            }

            uint width = ReadUInt32BigEndian(png, 16);
            uint height = ReadUInt32BigEndian(png, 20);
            if (width != 320 || height != 960)
            {
                throw new ArgumentException(string.Format(
                    "Panel PNG must be exactly 320x960; received {0}x{1}.", width, height), "png");
            }

            if (png[24] != 8 || png[25] != 6)
            {
                throw new ArgumentException(string.Format(
                    "Panel PNG must be 8-bit RGBA (color type 6); received bit depth {0}, color type {1}.",
                    png[24], png[25]), "png");
            }

            if (png[26] != 0 || png[27] != 0 || png[28] != 0)
            {
                throw new ArgumentException(
                    "Panel PNG must use standard compression/filtering and must not be interlaced.", "png");
            }
        }

        private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }
    }
}
