using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GuGuGaGaTranslator.Installer;

/// <summary>Unicode shell links, independent of Windows Script Host and the system ANSI code page.</summary>
internal static class WindowsShortcut
{
    public static void Write(string path, string target, string workingDirectory)
    {
        // The graphical installer runs file copying on a worker; Shell COM work uses an STA.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { Write(path, target, workingDirectory); } catch (Exception error) { failure = error; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(); thread.Join();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }

        path = Path.GetFullPath(path);
        target = Path.GetFullPath(target);
        workingDirectory = Path.GetFullPath(workingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
        var instance = Activator.CreateInstance(shellLinkType)!;
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(target);
            link.SetWorkingDirectory(workingDirectory);
            link.SetIconLocation(target, 0);
            link.SetDescription("屏幕实时翻译");
            ((IPersistFile)instance).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    // Vtable order from the Windows SDK IShellLinkW declaration (shobjidl_core.h).
    // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int characters, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int characters);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int characters);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int characters);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int characters, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
