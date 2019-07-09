using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Wipe
{
    public class DiskInfo
    {
        public readonly string SerialNumber;
        public readonly string MediaType;
        public readonly string Model;
        public readonly string Path;
        public readonly string PNPDeviceID;
        public readonly ulong Size;
        public readonly DiskGeometry Geometry;

        public DiskInfo(ManagementObject DiskObject)
        {
            PNPDeviceID = DiskObject.Properties["PNPDeviceID"].Value.ToString();
            Model = DiskObject.Properties["Model"].Value.ToString();
            MediaType = DiskObject.Properties["MediaType"].Value.ToString();
            SerialNumber = DiskObject.Properties["SerialNumber"].Value.ToString();
            Path = DiskObject.Properties["DeviceID"].Value.ToString();
            Size = (ulong)DiskObject.Properties["Size"].Value;
            Geometry = new DiskGeometry(DiskObject);
        }
    }

    public class DiskGeometry
    {
        public readonly uint BytesPerSector;
        public readonly uint SectorsPerTrack;
        public readonly uint TracksPerCylinder;
        public readonly ulong TotalCylinders;
        public readonly uint TotalHeads;
        public readonly ulong TotalSectors;
        public readonly ulong TotalTracks;

        public DiskGeometry(ManagementObject DiskObject)
        {
            BytesPerSector = (uint)DiskObject.Properties["BytesPerSector"].Value;
            SectorsPerTrack = (uint)DiskObject.Properties["SectorsPerTrack"].Value;
            TracksPerCylinder = (uint)DiskObject.Properties["TracksPerCylinder"].Value;
            TotalCylinders = (ulong)DiskObject.Properties["TotalCylinders"].Value;
            TotalHeads = (uint)DiskObject.Properties["TotalHeads"].Value;
            TotalSectors = (ulong)DiskObject.Properties["TotalSectors"].Value;
            TotalTracks = (ulong)DiskObject.Properties["TotalTracks"].Value;
        }
    }

    public static class Drives
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] FileAccess access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
            IntPtr templateFile);

        public static FileStream OpenDisk(string FileName, bool Readonly)
        {
            var Mode = Readonly ? FileAccess.Read : FileAccess.ReadWrite;
            var Ptr = CreateFile(
                FileName, Mode,
                FileShare.ReadWrite, IntPtr.Zero,
                FileMode.Open, FileAttributes.Normal,
                IntPtr.Zero);
            if (Ptr != IntPtr.Zero)
            {
                return new FileStream(new SafeFileHandle(Ptr, true), Mode);
            }
            throw new Win32Exception();
        }

        public static DiskInfo[] GetDrives()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
            {
                return searcher.Get()
                    .OfType<ManagementObject>()
                    .Select(o => new DiskInfo(o))
                    .ToArray();
            }
        }

        public static string FormatSize(double size)
        {
            var Sizes = "B,KB,MB,GB,TB,PB,EB,ZB,YB,WTF".Split(',');
            int index = 0;
            while (size >= 1024.0 && ++index < Sizes.Length - 1)
            {
                size /= 1024.0;
            }
            return string.Format("{0} {1}",
                //Possible formats:
                //X.XXX
                //XX.XX
                //XXX.X
                //XXXX
                Math.Round(size, 4 - Math.Floor(size).ToString().Length),
                Sizes[index]);
        }
    }
}
