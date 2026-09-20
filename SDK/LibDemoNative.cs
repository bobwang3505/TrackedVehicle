// 人工维护的 P/Invoke 声明，对应 Native/linux-arm64/include/demo_api.h 和 demo_data.h。
// 原生 ABI 变化时，手动同步函数签名、结构体布局和回调定义；维护步骤见 Native/README.md。

using System.Runtime.InteropServices;

namespace TrackedVehicle.SDK;

internal static class LibDemoNative
{
    internal const string LogicalLibraryName = "TrackedVehicle.NativeAlgorithm";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AvStatusCallback(int id, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AvFrameIndexCallback(int id, long frameIndex);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AvStreamCallback(
        int id,
        IntPtr packet,
        long frameIndex,
        int type,
        int key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AvMediaFinishCallback(
        int id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        long startTime,
        long endTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct CameraConfig
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        internal string Device;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        internal string Alias;

        internal int ParentCameraId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct MediaInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        internal string Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RoiInfo
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DetectionInfo
    {
        internal int BoxX;
        internal int BoxY;
        internal int BoxWidth;
        internal int BoxHeight;
        internal int Valid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DetectionGroupInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        internal DetectionInfo[] Detections;

        internal int Count;
    }

    [DllImport(
        LogicalLibraryName,
        EntryPoint = "Demo_OpenCam",
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern int OpenCamera(
        ref CameraConfig config,
        AvFrameIndexCallback frameIndexCallback,
        AvStreamCallback streamCallback,
        AvStatusCallback statusCallback,
        ref int cameraId);

    [DllImport(
        LogicalLibraryName,
        EntryPoint = "Demo_CloseCam",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CloseCamera(int cameraId);

    [DllImport(
        LogicalLibraryName,
        EntryPoint = "Demo_StartRealTimeRecord",
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern int StartRealTimeRecord(
        int cameraId,
        ref MediaInfo mediaInfo,
        int duration,
        AvMediaFinishCallback finishCallback,
        ref int recordId);

    [DllImport(
        LogicalLibraryName,
        EntryPoint = "Demo_StopRealTimeRecord",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int StopRealTimeRecord(int recordId);

    [DllImport(
        LogicalLibraryName,
        EntryPoint = "Demo_Detect",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Detect(
        int cameraId,
        in RoiInfo roi,
        ref DetectionGroupInfo detectionGroup);
}
