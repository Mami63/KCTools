using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace KCTools.Services
{
    /// <summary>
    /// WinUSB/SetupAPI P/Invoke katmanı. Yalnızca Windows'ta çalışır.
    /// Donanım olmadan işlevsel doğrulama yapılamaz.
    /// </summary>
    public sealed class WinUsbSession : IDisposable
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x80;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint ErrorIoPending = 997;
        private const uint PipeTransferTimeout = 0x03;

        private IntPtr _handle;
        private IntPtr _winUsb;
        private byte _pipeIn;
        private byte _pipeOut;
        private readonly ManualResetEvent _event = new ManualResetEvent(false);

        public string DevicePath { get; private set; }

        public ushort VendorId { get; private set; }

        public ushort ProductId { get; private set; }

        private WinUsbSession()
        {
        }

        public static WinUsbSession Open(
            ushort vendorId = 0x05C6, ushort productId = 0x9008)
        {
            foreach (var path in EnumerateDevicePaths())
            {
                WinUsbSession session = null;

                try
                {
                    session = new WinUsbSession { DevicePath = path };
                    session.Initialize();

                    if (session.VendorId == vendorId && session.ProductId == productId)
                    {
                        return session;
                    }
                }
                catch (Exception)
                {
                    // sıradaki cihazı dene
                }

                session?.Dispose();
            }

            throw new InvalidOperationException(
                "WinUSB EDL cihazı bulunamadı (VID_" + vendorId.ToString("X4") +
                " PID_" + productId.ToString("X4") + ").");
        }

        private void Initialize()
        {
            _handle = CreateFile(
                DevicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal | FileFlagOverlapped,
                IntPtr.Zero);

            if (_handle == new IntPtr(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cihaz açılamadı.");
            }

            if (!WinUsb_Initialize(_handle, out _winUsb))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize başarısız.");
            }

            var descriptor = new byte[18];
            uint transferred;

            if (!WinUsb_GetDescriptor(
                    _winUsb, 0x01, 0, 0, descriptor, (uint)descriptor.Length, out transferred))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "USB tanımlayıcısı okunamadı.");
            }

            VendorId = (ushort)(descriptor[8] | (descriptor[9] << 8));
            ProductId = (ushort)(descriptor[10] | (descriptor[11] << 8));

            var interfaceDescriptor = new UsbInterfaceDescriptor
            {
                bLength = (byte)Marshal.SizeOf<UsbInterfaceDescriptor>()
            };

            if (!WinUsb_QueryInterfaceSettings(_winUsb, 0, ref interfaceDescriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Arayüz ayarları okunamadı.");
            }

            for (byte index = 0; index < interfaceDescriptor.bNumEndpoints; index++)
            {
                var pipe = new WinUsbPipeInformation();

                if (!WinUsb_QueryPipe(_winUsb, 0, index, ref pipe))
                {
                    continue;
                }

                if (pipe.PipeType != 2)
                {
                    continue;
                }

                if ((pipe.PipeId & 0x80) != 0)
                {
                    _pipeIn = pipe.PipeId;
                }
                else
                {
                    _pipeOut = pipe.PipeId;
                }
            }

            if (_pipeIn == 0 || _pipeOut == 0)
            {
                throw new InvalidOperationException("Bulk IN/OUT endpoint bulunamadı.");
            }

            var timeout = 5000u;
            WinUsb_SetPipePolicy(_winUsb, _pipeIn, PipeTransferTimeout, 4, ref timeout);
            WinUsb_SetPipePolicy(_winUsb, _pipeOut, PipeTransferTimeout, 4, ref timeout);
        }

        public void Write(byte[] data, int offset, int count)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);

            var overlapped = new NativeOverlapped
            {
                EventHandle = _event.SafeWaitHandle.DangerousGetHandle()
            };

            _event.Reset();

            if (!WinUsb_WritePipe(
                    _winUsb, _pipeOut, buffer, (uint)buffer.Length, out var transferred,
                    ref overlapped))
            {
                var error = Marshal.GetLastWin32Error();

                if (error != ErrorIoPending)
                {
                    throw new Win32Exception(error, "WinUSB yazma başarısız.");
                }

                if (!GetOverlappedResult(_handle, ref overlapped, out transferred, true))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "WinUSB yazma tamamlanamadı.");
                }
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var temp = new byte[count];

            var overlapped = new NativeOverlapped
            {
                EventHandle = _event.SafeWaitHandle.DangerousGetHandle()
            };

            _event.Reset();

            if (!WinUsb_ReadPipe(
                    _winUsb, _pipeIn, temp, (uint)temp.Length, out var transferred,
                    ref overlapped))
            {
                var error = Marshal.GetLastWin32Error();

                if (error != ErrorIoPending)
                {
                    throw new Win32Exception(error, "WinUSB okuma başarısız.");
                }

                if (!GetOverlappedResult(_handle, ref overlapped, out transferred, true))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "WinUSB okuma tamamlanamadı.");
                }
            }

            var read = (int)Math.Min(transferred, (uint)count);
            Buffer.BlockCopy(temp, 0, buffer, offset, read);
            return read;
        }

        private static IEnumerable<string> EnumerateDevicePaths()
        {
            var interfaceGuid = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
            var deviceInfo = SetupDiGetClassDevs(
                ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, 0x12);

            if (deviceInfo == new IntPtr(-1))
            {
                yield break;
            }

            try
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    cbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                for (uint index = 0; ; index++)
                {
                    if (!SetupDiEnumDeviceInterfaces(
                            deviceInfo, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                    {
                        yield break;
                    }

                    if (!SetupDiGetDeviceInterfaceDetail(
                            deviceInfo, ref interfaceData, IntPtr.Zero, 0, out var required,
                            IntPtr.Zero))
                    {
                        // ERROR_INSUFFICIENT_BUFFER beklenir
                    }

                    var buffer = Marshal.AllocHGlobal(required);

                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

                        if (SetupDiGetDeviceInterfaceDetail(
                                deviceInfo, ref interfaceData, buffer, required, out _,
                                IntPtr.Zero))
                        {
                            var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));

                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                yield return path;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfo);
            }
        }

        public void Dispose()
        {
            if (_winUsb != IntPtr.Zero)
            {
                WinUsb_Free(_winUsb);
                _winUsb = IntPtr.Zero;
            }

            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }

            _event.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct UsbInterfaceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinUsbPipeInformation
        {
            public int PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte MaximumTransferSize;
            public byte Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
            uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
            out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(
            IntPtr handle, ref NativeOverlapped overlapped, out uint transferred, bool wait);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(IntPtr deviceHandle, out IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_GetDescriptor(
            IntPtr interfaceHandle, byte descriptorType, byte index, ushort languageId,
            byte[] buffer, uint bufferLength, out uint lengthTransferred);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryInterfaceSettings(
            IntPtr interfaceHandle, byte alternateInterfaceNumber,
            ref UsbInterfaceDescriptor descriptor);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryPipe(
            IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex,
            ref WinUsbPipeInformation pipeInformation);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPipePolicy(
            IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength,
            ref uint value);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(
            IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength,
            out uint lengthTransferred, ref NativeOverlapped overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(
            IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength,
            out uint lengthTransferred, ref NativeOverlapped overlapped);
    }
}
