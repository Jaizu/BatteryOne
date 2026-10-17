using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryOne
{
    public enum HidQueryResult { Ok, DeviceNotFound, NoResponse }

    /// <summary>Minimal HID access through hid.dll and setupapi.dll.</summary>
    public static class Hid
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        const int DIGCF_PRESENT_INTERFACE = 0x12;
        const int HIDP_STATUS_SUCCESS = 0x00110000;

        static List<string> GetDevicePaths(ushort vid, ushort pid)
        {
            var result = new List<string>();
            string match = string.Format("vid_{0:x4}&pid_{1:x4}", vid, pid);
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT_INTERFACE);
            if (set == new IntPtr(-1)) return result;
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (path != null && path.ToLowerInvariant().Contains(match)) result.Add(path);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        static bool GetCaps(string path, out HIDP_CAPS caps)
        {
            caps = new HIDP_CAPS();
            using (var h = CreateFile(path, 0, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return false;
                if (!HidD_GetPreparsedData(h, out IntPtr pd)) return false;
                try { return HidP_GetCaps(pd, out caps) == HIDP_STATUS_SUCCESS; }
                finally { HidD_FreePreparsedData(pd); }
            }
        }

        /// <summary>
        /// Sends <paramref name="request"/> as a feature report to the device collection that has 64-byte
        /// feature reports, then waits for an input report starting with <paramref name="responsePrefix"/>.
        /// </summary>
        public static HidQueryResult Query(ushort vid, ushort pid, byte[] request, byte[] responsePrefix, int timeoutMs, out byte[] response)
        {
            response = null;
            string target = null;
            HIDP_CAPS caps = new HIDP_CAPS();
            foreach (string path in GetDevicePaths(vid, pid))
            {
                if (GetCaps(path, out caps) && caps.FeatureReportByteLength >= 64) { target = path; break; }
            }
            if (target == null) return HidQueryResult.DeviceNotFound;

            using (var h = CreateFile(target, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (h.IsInvalid) return HidQueryResult.DeviceNotFound;
                var feature = new byte[caps.FeatureReportByteLength];
                Array.Copy(request, feature, Math.Min(request.Length, feature.Length));
                if (!HidD_SetFeature(h, feature, feature.Length)) return HidQueryResult.NoResponse;

                using (var stream = new FileStream(h, FileAccess.ReadWrite, 1, true))
                {
                    var buffer = new byte[caps.InputReportByteLength];
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (true)
                    {
                        int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                        if (remaining <= 0) return HidQueryResult.NoResponse;
                        var read = stream.ReadAsync(buffer, 0, buffer.Length);
                        // A pending read is cancelled when the handle is closed
                        if (!read.Wait(remaining)) return HidQueryResult.NoResponse;
                        int n = read.Result;
                        if (n < responsePrefix.Length) continue;
                        bool matches = true;
                        for (int i = 0; i < responsePrefix.Length; i++)
                        {
                            if (buffer[i] != responsePrefix[i]) { matches = false; break; }
                        }
                        if (!matches) continue;
                        response = new byte[n];
                        Array.Copy(buffer, response, n);
                        return HidQueryResult.Ok;
                    }
                }
            }
        }
    }
}
