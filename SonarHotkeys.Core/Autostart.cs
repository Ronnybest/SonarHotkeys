using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SonarHotkeys;

/// <summary>
/// Starting with Windows through a shortcut in the user's Startup folder; no administrator rights are needed.
/// A shortcut rather than a Run registry value: the MSI can delete a file on uninstall in the user's profile, while
/// its custom actions do not see the user's registry hive. The folder is a parameter so checks can use a scratch one.
/// </summary>
public sealed partial class Autostart(string? folder = null)
{
    public const string Arguments = "--tray";
    private const string FileName = "SonarHotkeys.lnk";
    private readonly string _path = Path.Combine(folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup), FileName);

    public bool IsEnabled => File.Exists(_path);

    public void Enable(string executablePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var link = CreateShellLink();
        link.SetPath(executablePath);
        link.SetArguments(Arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);
        link.SetDescription("SonarHotkeys");
        ((IPersistFile)link).Save(_path, true);
    }

    public void Disable()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    /// <summary>Points an existing shortcut at this executable, e.g. after the portable folder was moved; returns true if it changed.</summary>
    public bool Refresh(string executablePath)
    {
        if (!IsEnabled) return false;
        var (target, arguments) = Read();
        if (string.Equals(target, executablePath, StringComparison.OrdinalIgnoreCase) && arguments == Arguments) return false;
        Enable(executablePath);
        return true;
    }

    /// <summary>The shortcut's target and arguments.</summary>
    public unsafe (string Target, string Arguments) Read()
    {
        var link = CreateShellLink();
        ((IPersistFile)link).Load(_path, 0);
        const int capacity = 1024;
        char* buffer = stackalloc char[capacity];
        link.GetPath(buffer, capacity, 0, 0);
        string target = new(buffer);
        link.GetArguments(buffer, capacity);
        return (target, new string(buffer));
    }

    private static IShellLinkW CreateShellLink()
    {
        Guid clsid = new("00021401-0000-0000-C000-000000000046"), iid = typeof(IShellLinkW).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, iid, out nint pointer));
        try { return (IShellLinkW)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None); }
        finally { Marshal.Release(pointer); }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    // Declared in vtable order; only the members used above are called.
    [GeneratedComInterface, Guid("000214F9-0000-0000-C000-000000000046")]
    internal unsafe partial interface IShellLinkW
    {
        void GetPath(char* file, int capacity, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription(char* name, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(char* directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(char* arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(char* iconPath, int capacity, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [GeneratedComInterface, Guid("0000010b-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile(out nint fileName);
    }
}
