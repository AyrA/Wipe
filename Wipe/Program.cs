using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Wipe
{
    public class Program
    {
        /// <summary>
        /// Ways of overwriting data
        /// </summary>
        public enum EraseMode
        {
            /// <summary>
            /// Writes all zeros
            /// </summary>
            Zero,
            /// <summary>
            /// Writes all ones
            /// </summary>
            One,
            /// <summary>
            /// Writes an alternating pattern of ones and zeros
            /// </summary>
            Alternate,
            /// <summary>
            /// Writes random data
            /// </summary>
            Random,
            /// <summary>
            /// Writes cryptographically secure random data
            /// </summary>
            RandomCrypto
        }

        /// <summary>
        /// Provides application settings
        /// </summary>
        [DataContract]
        public class Settings
        {
            /// <summary>
            /// Saved progress (serial number is key)
            /// </summary>
            [DataMember]
            public Dictionary<string, long> Progress;
            /// <summary>
            /// Allow fixed disks in the selection
            /// </summary>
            [DataMember]
            public bool AllowFixedDisk;
            /// <summary>
            /// Overwrite mode
            /// </summary>
            [DataMember]
            public EraseMode Mode;

            /// <summary>
            /// Gets if the supplied disk is suitable for overwriting
            /// </summary>
            /// <param name="I">Disk</param>
            /// <returns>true if allowed to overwrite</returns>
            public bool CanWipe(DiskInfo I)
            {
                var L = I.MediaType.ToLower();
                return !L.Contains("unknown") &&
                    (AllowFixedDisk ||
                    L.Contains("removable") ||
                    L.Contains("floppy"));
            }

            /// <summary>
            /// Gets description string of a mode
            /// </summary>
            /// <param name="Mode">Mode</param>
            /// <returns>Description string</returns>
            public static string GetModeDescription(EraseMode Mode)
            {
                switch (Mode)
                {
                    case EraseMode.Zero:
                        return "Write all zeros";
                    case EraseMode.Alternate:
                        return "Write alternating pattern of one and zero";
                    case EraseMode.One:
                        return "Write all ones";
                    case EraseMode.Random:
                        return "Write random data";
                    case EraseMode.RandomCrypto:
                        return "Write cryptographically secure random data";
                    default:
                        throw new NotImplementedException($"The mode {Mode} is not defined");
                }
            }
        }

        /// <summary>
        /// Main Handler
        /// </summary>
        /// <param name="args">Command line arguments</param>
        static void Main(string[] args)
        {
            Console.Title = "Disk Wipe";
            var S = LoadSettings();
            var Item = MainMenu(S);

            //Save settings on CTRL+C before exiting
            Console.CancelKeyPress += delegate
            {
                Console.WriteLine("User abort.");
                SaveSettings(S);
            };

            if (Item == null)
            {
                SaveSettings(S);
                return;
            }

            Console.Clear();
            E();
            Console.Beep();
            Line("");
            Line("-- DATA LOSS IMMINENT --");
            Line(string.Format("SELECTED DISK: {0} {1}", Item.Path, Item.Model));
            Line("YOU ARE ABOUT TO COMPLETELY DESTROY ALL CONTENTS OF THE SELECTED DISK.");
            Line("THERE IS NO CHANCE OF RECOVERY.");
            Line("THIS IS THE LAST CHANCE TO AVOID DATA LOSS. CONTINUE?");
            if (S.Progress.ContainsKey(Item.SerialNumber))
            {
                Line("");
                Line($"You have saved progress. It will continue at {Drives.FormatSize(S.Progress[Item.SerialNumber])}.");
                Line("If you don't want this, clear the progress cache in the settings.");
            }
            Line("");
            Console.Beep();
            Console.ResetColor();
            if (Menu("Yes, DESTROY ALL DATA|No, cancel the operation and exit".Split('|'), 1, true) == 0)
            {
                if (S.CanWipe(Item))
                {
                    using (var FS = Drives.OpenDisk(Item.Path, false))
                    {
                        Console.Title = "Disk Wipe -- IN PROGRESS";
                        //Use a small write buffer for drives with less than 1GB
                        byte[] Test = new byte[Item.Size >= 1000000000 ? 1024 * 1024 : 512];
                        switch (S.Mode)
                        {
                            case EraseMode.Zero:
                                //Do nothing, bytes are zero by default
                                break;
                            case EraseMode.Alternate:
                                Test.Select(m => (byte)0xAA).ToArray();
                                break;
                            case EraseMode.One:
                                Test.Select(m => (byte)0xFF).ToArray();
                                break;
                            case EraseMode.Random:
                            case EraseMode.RandomCrypto:
                                //This is handled in the loop itself
                                break;
                            default:
                                throw new NotImplementedException($"Mode {S.Mode} is not implemented");
                        }
                        using (var R = new RNG(S.Mode == EraseMode.RandomCrypto))
                        {
                            var Progress = new Stopwatch();
                            var SW = new Stopwatch();
                            var AutoSave = new Stopwatch();
                            var Cont = true;
                            var DamagedBytes = 0L;
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
                            Console.WriteLine("Erasing drive at {0} ...", Item.Path);
                            Console.WriteLine("Press [ESC] to cancel at any time");
                            Progress.Start();
                            SW.Start();
                            AutoSave.Start();
                            while (Cont && (ulong)FS.Position < Item.Size)
                            {
                                //Get random bytes if needed
                                if (S.Mode == EraseMode.Random || S.Mode == EraseMode.RandomCrypto)
                                {
                                    R.NextBytes(Test);
                                }
                                //Number of bytes we want to write this time.
                                //Ensure we do not overshoot the stream length.
                                var Count = (int)Math.Min((ulong)Test.Length, Item.Size - (ulong)FS.Position);
                                try
                                {
                                    //Write normally
                                    FS.Write(Test, 0, Count);
                                }
                                catch
                                {
                                    //Try to weasel around damaged bytes.
                                    DamagedBytes += SlowWrite(FS, Test, 0, Count);
                                }
                                //Write status line once a second
                                if (Progress.ElapsedMilliseconds >= 1000)
                                {
                                    var P = Perc(FS.Position, Item.Size, false);
                                    var Line = string.Format("{0:000.00}% {1,10}/{2,-10} {3}",
                                        P,
                                        Drives.FormatSize(FS.Position),
                                        Drives.FormatSize(Item.Size),
                                        TimeEstimate(SW.ElapsedMilliseconds, P));
                                    Console.Write(Line.PadRight(Console.BufferWidth - 1));
                                    Console.CursorLeft = 0;

                                    //Auto-save progress once a minute
                                    //We don't actually care if this is successful or not
                                    if (AutoSave.Elapsed.TotalMinutes >= 1.0)
                                    {
                                        S.Progress[Item.SerialNumber] = FS.Position;
                                        SaveSettings(S);
                                        AutoSave.Restart();
                                    }

                                    //Check for [ESC] key
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
                                Console.WriteLine("Operation cancelled. Progress will be saved");
                                S.Progress[Item.SerialNumber] = FS.Position;
                            }
                            else if (S.Progress.ContainsKey(Item.SerialNumber))
                            {
                                S.Progress.Remove(Item.SerialNumber);
                            }
                            //Show damage report
                            if (DamagedBytes > 0)
                            {
                                E();
                                Line("-- DAMAGED DISK/VOLUME --");
                                Line($"The selected disk/volume had trouble writing {DamagedBytes} bytes.");
                                Line("Recommendation: Discard this media.");
                                Line("If you have to use it again, perform a full format or a surface check.");
                                Console.ResetColor();
                            }
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

        /// <summary>
        /// Writes individual bytes to navigate around errors
        /// </summary>
        /// <param name="S">Stream</param>
        /// <param name="Data">Data to write</param>
        /// <param name="Start">Offset in <paramref name="Data"/></param>
        /// <param name="Count">Number of bytes to write</param>
        /// <returns>Number of bytes that failed to write</returns>
        /// <remarks>Failed bytes are seeked over</remarks>
        private static int SlowWrite(Stream S, byte[] Data, int Start, int Count)
        {
            if (S == null)
            {
                throw new ArgumentNullException(nameof(S));
            }
            if (!S.CanSeek)
            {
                throw new NotSupportedException("The supplied stream must be seekable for this operation.");
            }
            if (Data == null)
            {
                throw new ArgumentNullException(nameof(Data));
            }
            if (Start < 0 || Start >= Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(Start));
            }
            if (Count < 0 || Count + Start > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(Count));
            }
            var damaged = 0;
            var ExpectedEnd = S.Position + Count;
            for (var i = 0; i < Count; i++)
            {
                long Pos = S.Position;
                try
                {
                    S.Write(Data, Start + i, 1);
                    S.Flush();
                }
                catch
                {
                    //Only seek forward if the position has not changed in the last write attempt.
                    //Sometimes it does and only fails on the S.Flush() call
                    if (S.Position == Pos)
                    {
                        S.Seek(1, SeekOrigin.Current);
                    }
                    ++damaged;
                }
            }
            if (S.Position != ExpectedEnd)
            {
                S.Seek(ExpectedEnd, SeekOrigin.Begin);
            }
            return damaged;
        }

        /// <summary>
        /// Sets "Error" color scheme
        /// </summary>
        private static void E()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.Red;
        }

        /// <summary>
        /// Writes a line by filling up the entire horizontal buffer
        /// </summary>
        /// <param name="s">Text to write</param>
        /// <remarks>Will not process line breaks</remarks>
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

        /// <summary>
        /// Shows the main menu
        /// </summary>
        /// <param name="CurrentSettings">Current settings</param>
        /// <returns>Selected Disk, or null if none was selected</returns>
        private static DiskInfo MainMenu(Settings CurrentSettings)
        {
            var Item = 0;
            while (true)
            {
                DiskInfo SelectedDisk = null;
                Console.Clear();
                Console.WriteLine(@"Disk Wipe Utility
Select an option or press [ESC] to exit
");
                Item = Menu("Delete entire drive|Delete single partition|Settings|Help".Split('|'), Item, true);
                switch (Item)
                {
                    case -1:
                        return null;
                    case 0:
                        SelectedDisk = SelectPhysicalDisk(CurrentSettings);
                        break;
                    case 1:
                        SelectedDisk = SelectVolume(CurrentSettings);
                        break;
                    case 2:
                        DoSettings(CurrentSettings);
                        break;
                    case 3:
                        Help(CurrentSettings);
                        break;
                    default:
                        throw new NotImplementedException();
                }
                if (SelectedDisk != null)
                {
                    return SelectedDisk;
                }
            }
        }

        /// <summary>
        /// Prompts the user to select a partition/volume
        /// </summary>
        /// <param name="CurrentSettings">Current settings</param>
        /// <returns>Selected Disk, or null if none was selected</returns>
        private static DiskInfo SelectVolume(Settings CurrentSettings)
        {
            while (true)
            {
                try
                {
                    Console.WriteLine("Loading volumes...");
                    return SelectDiskFromList(
                        Drives.GetVolumes().OrderBy(m => m.Path).ToArray(),
                        CurrentSettings);
                }
                catch
                {
                    //NOOP
                }
            }
        }

        /// <summary>
        /// Prompts the user to select a physical volume
        /// </summary>
        /// <param name="CurrentSettings">Current settings</param>
        /// <returns>Selected Disk, or null if none was selected</returns>
        private static DiskInfo SelectPhysicalDisk(Settings CurrentSettings)
        {
            while (true)
            {
                try
                {
                    Console.WriteLine("Loading disk drives...");
                    return SelectDiskFromList(
                        Drives.GetPhysicalDrives().OrderBy(m => m.Path).ToArray(),
                        CurrentSettings);
                }
                catch
                {
                    //NOOP
                }
            }
        }

        /// <summary>
        /// Prompts the user to select a disk from the given list
        /// </summary>
        /// <param name="DriveList">List of selectable drives</param>
        /// <param name="CurrentSettings">Current settings</param>
        /// <returns>Selected Disk, or null if none was selected</returns>
        /// <remarks>Throws an exception if the user requests a refresh</remarks>
        private static DiskInfo SelectDiskFromList(DiskInfo[] DriveList, Settings CurrentSettings)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine(@"Disk Wipe Utility
Select disk or press [ESC] to go back
");
                var MenuItems = DriveList.Select(m => string.Format("{0,-9}\t{1,-20}\t{2}",
                    Drives.FormatSize(m.Size),
                    m.SerialNumber, m.Model)).Concat(new string[] { "<Refresh List>" }).ToArray();
                var Item = Menu(MenuItems, 0, true);
                if (Item == -1)
                {
                    return null;
                }
                if (Item == DriveList.Length)
                {
                    throw new Exception("Refresh");
                }
                if (Item < DriveList.Length)
                {
                    var Selected = DriveList[Item];
                    if (CurrentSettings.CanWipe(Selected))
                    {
                        return Selected;
                    }
                    else
                    {
                        Console.ResetColor();
                        Console.Clear();
                        E();
                        Line("Fixed disks are disallowed.");
                        Line("If you are sure you want to wipe this disk, change the settings.");
                        Console.ResetColor();
                        Console.WriteLine("Press any key to go back to the menu...");
                        Console.ReadKey(true);
                    }
                }
            }
        }

        /// <summary>
        /// Shows the menu that handles the settings
        /// </summary>
        /// <param name="S">Current settings</param>
        /// <returns><paramref name="S"/></returns>
        private static Settings DoSettings(Settings S)
        {
            var Item = 0;
            var ModeOrder = Enum.GetValues(typeof(EraseMode)).OfType<EraseMode>().ToList();

            while (true)
            {
                var Items = new string[]
                {
                    $"Clear Progress ({S.Progress.Count} items)",
                    S.AllowFixedDisk ? "Allow fixed disks: YES" : "Allow fixed disks: NO",
                    $"Mode: {Settings.GetModeDescription(S.Mode)}"
                };
                Console.Clear();
                Console.WriteLine(@"Disk Wipe Utility
Select an item or press [ESC] to go back
");
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
                    case 2:
                        S.Mode = ModeOrder[(ModeOrder.IndexOf(S.Mode) + 1) % ModeOrder.Count];
                        break;
                    default:
                        throw new NotImplementedException($"Option {Item} is not implemented");
                }
            }
        }

        /// <summary>
        /// Clears the keyboard buffer
        /// </summary>
        private static void FlushKeys()
        {
            while (Console.KeyAvailable && Console.ReadKey(true) != null) ;
        }

        /// <summary>
        /// Tries to save the settings
        /// </summary>
        /// <param name="S">Settings</param>
        /// <returns>true if saved, false on error</returns>
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

        /// <summary>
        /// Loads the settings
        /// </summary>
        /// <returns>Loaded settings, or default if failed</returns>
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

        /// <summary>
        /// Calculates time estimate
        /// </summary>
        /// <param name="ElapsedMS">Total time in milliseconds that elapsed so far</param>
        /// <param name="Percentage">Progress percentage</param>
        /// <returns>Time string. Format: [d.]hh:mm:ss</returns>
        /// <remarks>
        /// Try to supply the <paramref name="Percentage"/> as unrounded as possible
        /// to avoid the time jumping around.
        /// The algorithm gets more accurate the higher the percentage is.
        /// </remarks>
        private static string TimeEstimate(double ElapsedMS, double Percentage)
        {
            var TotalTime = Percentage == 0 ? 0 : 100.0 / Percentage * ElapsedMS;
            var Current = TimeSpan.FromSeconds(Math.Floor(ElapsedMS / 1000.0));
            var Total = TimeSpan.FromSeconds(Math.Floor(TotalTime / 1000.0));
            return string.Format("{0}/{1}", Current, Total);
        }

        /// <summary>
        /// Calculates the progress percentage giving a "current" value and the "maximum" value
        /// </summary>
        /// <param name="position">Current value of progress</param>
        /// <param name="size">Maximum value of progress (value that is considered 100%)</param>
        /// <param name="Round"></param>
        /// <returns></returns>
        private static double Perc(double position, double size, bool Round = true)
        {
            if (
                double.IsInfinity(size) || double.IsNaN(size) ||
                double.IsInfinity(position) || double.IsNaN(position))
            {
                throw new ArgumentException("None of the arguments can be \"Infinity\" or \"NaN\"");
            }
            //Force size to be positive
            size = Math.Abs(size);
            //Force position into 0% - 100% range.
            position = Math.Min(size, Math.Max(0.0, Math.Abs(position)));

            //Prefer 0% over 100%
            if (position == 0.0)
            {
                return 0.0;
            }
            if (position == size)
            {
                return 100.0;
            }
            //Calculate percentage and round as demanded
            var V = position / size * 100;
            return Round ? Math.Round(V, 2) : V;
        }

        /// <summary>
        /// Shows a menu with keyboard selectable entries
        /// </summary>
        /// <param name="Options">Possible options</param>
        /// <param name="Default">Default option (Zero is first option)</param>
        /// <param name="AllowCancel">Allow to cancel with [ESC]</param>
        /// <returns>Selected option index, or -1 if cancelled</returns>
        private static int Menu(string[] Options, int Default = 0, bool AllowCancel = false)
        {
            int Current = Default;
            //Argument validation
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

            //Remember menu position
            var Pos1 = new { X = Console.CursorLeft, Y = Console.CursorTop };
            while (true)
            {
                bool rewrite = false;
                Console.SetCursorPosition(Pos1.X, Pos1.Y);
                for (int i = 0; i < Options.Length; i++)
                {
                    Console.ResetColor();
                    Console.CursorLeft = 3;
                    //Change color of active item
                    if (i == Current)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.BackgroundColor = ConsoleColor.Blue;
                    }
                    Console.Write("[{0}] ", i == Current ? '>' : ' ');
                    Console.WriteLine(ClipLine(Options[i]));
                }
                //Rewrite the menu only when the selected option changes
                while (!rewrite)
                {
                    switch (Console.ReadKey(true).Key)
                    {
                        case ConsoleKey.Home:
                        case ConsoleKey.PageUp:
                            //First item
                            Current = 0;
                            rewrite = true;
                            break;
                        case ConsoleKey.End:
                        case ConsoleKey.PageDown:
                            //Last item
                            Current = Options.Length - 1;
                            rewrite = true;
                            break;
                        case ConsoleKey.UpArrow:
                            //Previous item
                            if (Current > 0)
                            {
                                --Current;
                                rewrite = true;
                            }
                            break;
                        case ConsoleKey.DownArrow:
                            //Next item
                            if (Current < Options.Length - 1)
                            {
                                ++Current;
                                rewrite = true;
                            }
                            break;
                        case ConsoleKey.Enter:
                            //Accept item
                            Console.ResetColor();
                            return Current;
                        case ConsoleKey.Escape:
                            //Cancel menu
                            if (AllowCancel)
                            {
                                Console.ResetColor();
                                return -1;
                            }
                            break;
                        default:
                            //Invalid key
                            Console.Beep();
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Clips a line of text if it's longer than the console buffer width
        /// </summary>
        /// <param name="Text">Text</param>
        /// <returns>Clipped (if necessary) line</returns>
        /// <remarks>
        /// This function takes the current cursor position into account.
        /// Clipping is done by cutting off excess text and adding "..."
        /// </remarks>
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

        /// <summary>
        /// Shows application help
        /// </summary>
        /// <param name="CurrentSettings">Currently used settings</param>
        private static void Help(Settings CurrentSettings)
        {
            var M = CurrentSettings.Mode;
            Console.Clear();
            Console.WriteLine(@"Disk Wipe
This utility wipes the data on the specified disk.

You can wipe entire physical disks, or individual volumes/partitions.

It will only let you overwrite removable USB media and floppy drives
by default. You can change this in the settings.

You can cancel the overwrite progress at any time.
The progress is then saved so you can continue it at a later time.

Multiple modes are available for selection.
The asterisk marks your currently selected mode.

{0}Zero:
Writes binary zeros over the entire disk.
This is done by writing the 0x00 byte value repeatedly.

{1}Alternate:
Writes alternating one and zero pattern to the disk.
This is done by writing the 0xAA byte value repeatedly.

{2}One:
Writes binary ones over the entire disk.
This is done by writing the 0xFF byte value repeatedly.

{3}Random:
Writes random data in a single pass.
Uses a purely mathematical random number generator.
Someone dedicated enough can figure out that the disk was overwritten
using a predictable generator. If this is a problem, use the
'RandomCrypto' method. The 'erase quality' is the same.

{4}RandomCrypto:
Writes random data in a single pass.
This uses a cryptographically secure random number generator.
This doesn't improves the 'erase quality' but merely prevents people from
figuring out that the disk was overwritten by a random number generator
because the sequence is unpredictable.
This method can be significantly slower than the regular random method.

Press any key to return",
M == EraseMode.Zero ? "*" : "",
M == EraseMode.Alternate ? "*" : "",
M == EraseMode.One ? "*" : "",
M == EraseMode.Random ? "*" : "",
M == EraseMode.RandomCrypto ? "*" : ""
);
            FlushKeys();
            Console.ReadKey(true);
        }
    }
}
