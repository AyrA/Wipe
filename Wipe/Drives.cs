using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Wipe
{
    public enum DriveType
    {
        Physical,
        Volume
    }

    /// <summary>
    /// Represents a physical drive
    /// </summary>
    public class DiskInfo
    {
        /// <summary>
        /// Manufacturer assigned serial number
        /// </summary>
        public readonly string SerialNumber;
        public readonly string MediaType;
        public readonly string Model;
        public readonly string Path;
        public readonly string PNPDeviceID;
        public readonly ulong Size;
        public readonly DiskGeometry Geometry;
        public readonly DriveType Type;

        public DiskInfo(ManagementObject DiskObject)
        {
            try
            {
                //Size is not available on ejected media (for example an empty CD-Rom drive)
                Size = (ulong)DiskObject.Properties["Size"].Value;
            }
            catch
            {
                Size = 0;
            }
            switch (DiskObject.ClassPath.ClassName)
            {
                case "Win32_LogicalDisk":
                    var MediaTypeInt = (uint)Tools.IntOrDefault(DiskObject.Properties["MediaType"].Value);
                    Type = DriveType.Volume;
                    MediaType = GetMediaTypeString(MediaTypeInt);
                    if (Size == 0 && MediaType.ToLower() == "floppy")
                    {
                        Size = GetMediaSize(MediaTypeInt);
                    }
                    Path = @"\\.\" + (string)DiskObject.Properties["DeviceID"].Value;
                    SerialNumber = (string)DiskObject.Properties["VolumeSerialNumber"].Value;
                    if (string.IsNullOrEmpty(SerialNumber))
                    {
                        SerialNumber = "00000000";
                    }
                    //Build Model string
                    Model = string.Format("{0} {1} {2}",
                        DiskObject.Properties["DeviceID"].Value,
                        Tools.StrOrDefault(DiskObject.Properties["FileSystem"].Value, "RAW"),
                        Tools.StrOrDefault(DiskObject.Properties["VolumeName"].Value, "<no label>"));
                    break;
                case "Win32_DiskDrive":
                    Type = DriveType.Physical;
                    MediaType = (string)DiskObject.Properties["MediaType"].Value;
                    Path = (string)DiskObject.Properties["DeviceID"].Value;
                    PNPDeviceID = (string)DiskObject.Properties["PNPDeviceID"].Value;
                    Model = (string)DiskObject.Properties["Model"].Value;
                    SerialNumber = (string)DiskObject.Properties["SerialNumber"].Value;
                    Geometry = new DiskGeometry(DiskObject);
                    break;
                default:
                    throw new NotSupportedException($"{DiskObject.ClassPath.ClassName} is not supported");
            }
        }

        private static ulong GetMediaSize(uint TypeInt)
        {
            switch (TypeInt)
            {
                //Format is SectorSize * SectorsPerTrack * Tracks * Sides;
                case 1:
                    return 512 * 15 * 80 * 2; //1.2  M
                case 2:
                    return 512 * 18 * 80 * 2; //1.44 M
                case 3:
                    return 512 * 36 * 80 * 2; //2.88 M
                case 4:
                    return 0; //WTF, 20.8 M
                case 5:
                    return 512 * 18 * 80 * 1; //720  K
                case 6:
                    return 512 * 9 * 40 * 2; //360   K
                case 7:
                    return 512 * 8 * 40 * 2; //320   K
                case 8:
                    return 1024 * 4 * 40 * 2; //320  K
                case 9:
                    return 512 * 9 * 40 * 1; //180   K
                case 10:
                    return 512 * 8 * 40 * 1; //160   K
                case 13:
                    return 0; //WTF, 120 M
                case 14:
                    return 512 * 8 * 80 * 2; //640   K
                case 15:
                    return 512 * 8 * 80 * 2; //640   K
                case 16:
                    return 512 * 9 * 80 * 2; //720   K
                case 17:
                    return 512 * 15 * 80 * 2; //1.2  M
                case 18:
                    return 1024 * 8 * 77 * 2; //1.23 M
                case 19:
                    return 1024 * 8 * 77 * 2; //1.23 M
                case 20:
                    return 0; //WTF, 128 M
                case 21:
                    return 0; //WTF, 230 M
                case 22:
                    return 1024 * 256; //8 inch 256K, unknown geometry
            }
            return 0;
        }

        private static string GetMediaTypeString(uint value)
        {
            switch ((int)value)
            {
                case 0:
                    return "Unknown";
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 13:
                case 14:
                case 15:
                case 16:
                case 17:
                case 18:
                case 19:
                case 20:
                case 21:
                case 22:
                    return "Floppy";
                case 11:
                    return "Removable or Network";
                case 12:
                    return "Fixed";
                default:
                    throw new NotImplementedException($"MediaType {value} is not implemented");
            }
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
            if (DiskObject.ClassPath.ClassName != "Win32_DiskDrive")
            {
                throw new NotSupportedException($"The WMI type {DiskObject.ClassPath.ClassName} is not supported");
            }
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
                FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero,
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
            return GetPhysicalDrives()
                .Concat(GetVolumes())
                .ToArray();
        }

        public static string FormatSize(double size)
        {
            const double FACTOR = 1000.0;
            var Sizes = "B,KB,MB,GB,TB,PB,EB,ZB,YB,WTF".Split(',');
            int index = 0;
            while (size >= FACTOR && ++index < Sizes.Length - 1)
            {
                size /= FACTOR;
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

        public static DiskInfo[] GetVolumes()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk"))
            {
                return searcher.Get()
                    .OfType<ManagementObject>()
                    .Select(o => new DiskInfo(o))
                    .ToArray();
            }
        }

        public static DiskInfo[] GetPhysicalDrives()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
            {
                return searcher.Get()
                    .OfType<ManagementObject>()
                    .Select(o => new DiskInfo(o))
                    .ToArray();
            }
        }
    }
}
