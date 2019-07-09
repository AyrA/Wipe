using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Wipe
{
    public class Program
    {
        public enum EraseMode
        {
            /// <summary>
            /// Writes all zeros
            /// </summary>
            x00,
            /// <summary>
            /// Writes all ones
            /// </summary>
            xFF,
            /// <summary>
            /// Writes an alternating pattern of ones and zeros
            /// </summary>
            xAA,
            /// <summary>
            /// Writes random data
            /// </summary>
            Rnd
        }

        [DataContract]
        public class Settings
        {
            [DataMember]
            public Dictionary<string, long> Progress;
            [DataMember]
            public bool AllowFixedDisk;
            [DataMember]
            public EraseMode Mode;

            public bool CanWipe(DiskInfo I)
            {
                return AllowFixedDisk || I.MediaType.ToLower().Contains("removable");
            }
        }

        static void Main(string[] args)
        {
            var S = LoadSettings();
            var DriveList = Drives.GetDrives().OrderBy(m => m.Path).ToArray();
            var Item = SelectDisk(S);

            if (Item == null)
            {
                SaveSettings(S);
                return;
            }

            Console.Clear();
            E();
            Line("YOU ARE ABOUT TO COMPLETELY DESTROY ALL CONTENTS OF THE SELECTED DISK.");
            Line("THIS IS THE LAST CHANCE TO AVOID DATA LOSS. CONTINUE?");
            if (S.Progress.ContainsKey(Item.SerialNumber))
            {
                Line($"Progress will continue at {Drives.FormatSize(S.Progress[Item.SerialNumber])}");
            }
            Console.ResetColor();
            if (Menu("Yes, DESTROY ALL DATA|No, cancel the operation".Split('|'), 1, true) == 0)
            {
                if (S.CanWipe(Item))
                {
                    using (var FS = Drives.OpenDisk(Item.Path, false))
                    {
                        byte[] Test = new byte[1024 * 1024];
                        switch (S.Mode)
                        {
                            case EraseMode.x00:
                                //Do nothing, bytes are zero by default
                                break;
                            case EraseMode.xAA:
                                Test.Select(m => (byte)0xAA).ToArray();
                                break;
                            case EraseMode.xFF:
                                Test.Select(m => (byte)0xFF).ToArray();
                                break;
                            case EraseMode.Rnd:
                                //This is handled in the loop itself
                                break;
                            default:
                                throw new NotImplementedException($"Mode {S.Mode} is not implemented");
                        }
                        var R = new Random();
                        var Progress = new Stopwatch();
                        var SW = new Stopwatch();
                        var Cont = true;
                        FlushKeys();
                        if (S.Progress.ContainsKey(Item.SerialNumber))
                        {
                            try
                            {
                                FS.Seek(S.Progress[Item.SerialNumber], SeekOrigin.Begin);
                            }
                            catch
                            {
                                E();
                                Line("Unable to continue wiping from the previous point.");
                                Line("This can be caused by disks with the same serial number.");
                                Line("Press any key to start from the beginning of the disk");
                                Console.ResetColor();
                                Console.ReadKey(true);
                            }
                        }
                        Console.Clear();
                        Console.WriteLine("Press [ESC] to cancel at any time");
                        Progress.Start();
                        SW.Start();
                        while (Cont && (ulong)FS.Position < Item.Size)
                        {
                            if (S.Mode == EraseMode.Rnd)
                            {
                                R.NextBytes(Test);
                            }
                            FS.Write(
                                Test, 0,
                                (int)Math.Min((ulong)Test.Length, Item.Size - (ulong)FS.Position));
                            if (Progress.ElapsedMilliseconds >= 1000)
                            {
                                var P = Perc(FS.Position, Item.Size, false);
                                var Line = string.Format("{0:000.00}% {1,10}/{2,-10} {3}",
                                    P,
                                    Drives.FormatSize(FS.Position),
                                    Drives.FormatSize(Item.Size),
                                    TimeEstimate(SW.ElapsedMilliseconds, P));
                                Console.Error.Write(Line.PadRight(Console.BufferWidth - 1));
                                Console.CursorLeft = 0;
                                while (Console.KeyAvailable)
                                {
                                    if (Console.ReadKey(true).Key == ConsoleKey.Escape)
                                    {
                                        Cont = false;
                                    }
                                }
                                Progress.Restart();
                            }
                        }
                        Console.Clear();
                        //Save progress if aborted, otherwise delete it
                        if (!Cont)
                        {
                            Console.Error.WriteLine("Operation cancelled. Progress will be saved");
                            S.Progress[Item.SerialNumber] = FS.Position;
                        }
                        else if (S.Progress.ContainsKey(Item.SerialNumber))
                        {
                            S.Progress.Remove(Item.SerialNumber);
                        }
                    }
                }
                else
                {
                    E();
                    Line("This is not a removable media!");
                    Console.ResetColor();
                }
            }
            else
            {
                E();
                Line("Operation aborted");
                Console.ResetColor();
            }
            FlushKeys();
            SaveSettings(S);
#if DEBUG
            Console.Error.WriteLine("#END");
            Console.ReadKey(true);
#endif
        }

        private static void E()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.Red;
        }

        private static void Line(string s)
        {
            if (s == null)
            {
                Line(string.Empty);
            }
            else if (s.Length > Console.BufferWidth)
            {
                Console.Write(s.Substring(0, Console.BufferWidth));
                Line(s.Substring(Console.BufferWidth));
            }
            else
            {
                Console.Write(s.PadRight(Console.BufferWidth));
            }
        }

        private static DiskInfo SelectDisk(Settings CurrentSettings)
        {
            var Item = 0;
            while (true)
            {
                var DriveList = Drives.GetDrives().OrderBy(m => m.Path).ToArray();
                var MenuItems = DriveList.Select(m => string.Format("{0,-9}\t{1,20}\t{2}",
                                    Drives.FormatSize(m.Size),
                                    m.SerialNumber, m.Model)).Concat(new string[] { "<Refresh List>", "<Settings>" }).ToArray();
                Console.Clear();
                Console.Error.WriteLine("Select disk or press [ESC] to exit");

                Item = Menu(MenuItems, Item, true);
                //Return selected item (or null if ESC was pressed)
                if (Item < DriveList.Length)
                {
                    if (Item < 0)
                    {
                        return null;
                    }
                    var DI = DriveList[Item];
                    if (CurrentSettings.CanWipe(DI))
                    {
                        return DriveList[Item];
                    }
                    else
                    {
                        Console.ResetColor();
                        Console.Clear();
                        E();
                        Line("Fixed disks are disallowed.");
                        Line("If you are sure you want to wipe this disk, change the settings.");
                        Console.ResetColor();
                        Console.Error.WriteLine("Press any key to go back to the menu...");
                        Console.ReadKey(true);
                        continue;
                    }
                }
                //Settings item
                if (Item == DriveList.Length + 1)
                {
                    DoSettings(CurrentSettings);
                }
                //The refresh item does nothing, this refreshes the list
                //Set Item back to 0 to 
                Item = 0;
            }
        }

        private static Settings DoSettings(Settings S)
        {
            var Item = 0;
            while (true)
            {
                var Items = new string[]
                {
                    $"Clear Progress ({S.Progress.Count} items)",
                    S.AllowFixedDisk ? "Allow fixed disks: YES" : "Allow fixed disks: NO"
                };
                Console.Clear();
                Console.Error.WriteLine("Select an item or press [ESC] to go back");
                Item = Menu(Items, Item, true);
                switch (Item)
                {
                    case -1:
                        return S;
                    case 0:
                        S.Progress.Clear();
                        break;
                    case 1:
                        S.AllowFixedDisk = !S.AllowFixedDisk;
                        break;
                    default:
                        throw new NotImplementedException($"Option {Item} is not implemented");
                }
            }
        }

        private static void FlushKeys()
        {
            while (Console.KeyAvailable && Console.ReadKey(true) != null) ;
        }

        private static bool SaveSettings(Settings S)
        {
            var Ser = new DataContractSerializer(typeof(Settings));
            try
            {
                using (var Proc = Process.GetCurrentProcess())
                {
                    using (var FS = File.Create(Path.Combine(Path.GetDirectoryName(Proc.MainModule.FileName), "settings.xml")))
                    {
                        using (var XMLW = new XmlTextWriter(FS, Encoding.UTF8))
                        {
                            XMLW.Formatting = Formatting.Indented;
                            Ser.WriteObject(XMLW, S);
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Settings LoadSettings()
        {
            try
            {
                var Ser = new DataContractSerializer(typeof(Settings));
                using (var Proc = Process.GetCurrentProcess())
                {
                    using (var FS = File.OpenRead(Path.Combine(Path.GetDirectoryName(Proc.MainModule.FileName), "settings.xml")))
                    {
                        var S = (Settings)Ser.ReadObject(FS);
                        //Make sure values are valid
                        if (S.Progress == null)
                        {
                            S.Progress = new Dictionary<string, long>();
                        }
                        return S;
                    }
                }
            }
            catch
            {
                return new Settings()
                {
                    Progress = new Dictionary<string, long>()
                };
            }
        }

        private static string TimeEstimate(double ElapsedMS, double Percentage)
        {
            var TotalTime = Percentage == 0 ? 0 : 100.0 / Percentage * ElapsedMS;
            var Current = TimeSpan.FromSeconds(Math.Floor(ElapsedMS / 1000.0));
            var Total = TimeSpan.FromSeconds(Math.Floor(TotalTime / 1000.0));
            return string.Format("{0}/{1}", Current, Total);
        }

        private static double Perc(double position, double size, bool Round = true)
        {
            var V = position / size * 100;
            return Round ? Math.Round(V, 2) : V;
        }

        private static int Menu(string[] Options, int Default = 0, bool AllowCancel = false)
        {
            int Current = Default;

            if (Options == null)
            {
                throw new ArgumentNullException("Options");
            }
            if (Options.Length == 0)
            {
                throw new ArgumentException("Options needs at least one entry");
            }

            if (Default < 0 || Default >= Options.Length)
            {
                throw new ArgumentOutOfRangeException("Default", $"Range: 0-{Options.Length - 1}");
            }
            var Pos1 = new { X = Console.CursorLeft, Y = Console.CursorTop };
            while (true)
            {
                bool rewrite = false;
                Console.SetCursorPosition(Pos1.X, Pos1.Y);
                for (int i = 0; i < Options.Length; i++)
                {
                    Console.ResetColor();
                    Console.CursorLeft = 3;
                    if (i == Current)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.BackgroundColor = ConsoleColor.Blue;
                    }
                    Console.Write("[{0}] ", i == Current ? '>' : ' ');
                    Console.WriteLine(ClipLine(Options[i]));
                }
                while (!rewrite)
                {
                    switch (Console.ReadKey(true).Key)
                    {
                        case ConsoleKey.PageUp:
                            Current = 0;
                            rewrite = true;
                            break;
                        case ConsoleKey.PageDown:
                            Current = Options.Length - 1;
                            rewrite = true;
                            break;
                        case ConsoleKey.UpArrow:
                            if (Current > 0)
                            {
                                --Current;
                                rewrite = true;
                            }
                            break;
                        case ConsoleKey.DownArrow:
                            if (Current < Options.Length - 1)
                            {
                                ++Current;
                                rewrite = true;
                            }
                            break;
                        case ConsoleKey.Enter:
                            Console.ResetColor();
                            return Current;
                        case ConsoleKey.Escape:
                            if (AllowCancel)
                            {
                                Console.ResetColor();
                                return -1;
                            }
                            break;
                    }
                }
            }
        }

        public static string ClipLine(string Text)
        {
            int max = Console.BufferWidth - Console.CursorLeft - 1;
            if (string.IsNullOrEmpty(Text))
            {
                return "";
            }
            if (Text.Length > max)
            {
                return Text.Substring(0, max - 3) + "...";
            }
            return Text;
        }
    }
}
