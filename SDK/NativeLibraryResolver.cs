using System.Reflection;
using System.Runtime.InteropServices;

namespace TrackedVehicle.SDK;

/// <summary>
/// 将稳定的 P/Invoke 逻辑库名解析到配置指定的动态库文件。
/// </summary>
internal static class NativeLibraryResolver
{
    private static int _initialized;
    private static string _libraryPath = string.Empty;

    internal static void Initialize(string libraryPath)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        _libraryPath = libraryPath;
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (libraryName != LibDemoNative.LogicalLibraryName)
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.Load(_libraryPath);
    }
}
