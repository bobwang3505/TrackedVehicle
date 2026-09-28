using System.Runtime.InteropServices;

namespace TrackedVehicle.NativeInterop
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AvStatusFunc(int id, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AvFrameIndexFunc(int id, long frmIdx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AvMediaFinishFunc(int id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, long startTime, long endTime);

    // 头文件定义了此回调，但当前没有注册入口；不要自行读取或释放 packet。
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AvStreamFunc(int id, IntPtr packet, long frmIdx, int type, int key);

    // Linux 下字符串按 UTF-8 封送，char[200] 最多存放 199 字节内容。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct CamCfg
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        public string chDev; // 相机地址

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        public string chAlias; // 相机别名
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MediaInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        public string chPath; // 录像路径
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RoiInfo
    {
        public int nX;
        public int nY;
        public int nWidth;
        public int nHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DetectInfo
    {
        public int nBoxX;
        public int nBoxY;
        public int nBoxWidth;
        public int nBoxHeight;
        public int valid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OrientationPosInfo
    {
        public int centerX;
    }

    /// <summary>C++ 摄像与算法接口，对应 RobotCamXApi.h 和 RobotCamXData.h。</summary>
    public class NativeMethods
    {
        internal const string DllName = @"LibRobotCamX.so";
        public const int MAX_COUNT = 10;

        /// <summary>打开相机，0 成功，负数失败。回调委托必须保存为字段直到原生端停止回调。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_OpenCam(ref CamCfg cfg, AvFrameIndexFunc frmIdxFunc,
            AvStatusFunc statusFunc, ref int camId);

        /// <summary>关闭相机，0 成功，负数失败。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_CloseCam(int camId);

        /// <summary>开始录像，duration 为分段秒数，返回 0 成功、负数失败；录像编号由 recId 输出。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_StartRealTimeRecord(int camId, ref MediaInfo mediaInfo,
            int duration, AvMediaFinishFunc finishFunc, ref int recId);

        /// <summary>停止录像，0 成功，负数失败。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_StopRealTimeRecord(int recId);

        /// <summary>调用原生 SDK 初始化模型，方向定位检测前需先成功调用。</summary>
        /// <param name="modelPath">运行 SDK 的设备上的模型路径，以 UTF-8 字符串传给原生接口。</param>
        /// <returns>0 成功，负数失败。</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_InitModel([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath);

        /// <summary>对指定原生相机的指定区域执行方向定位检测；调用前需初始化模型，并保持相机打开。</summary>
        /// <param name="camId">打开相机后获得的原生相机编号，不是配置中的业务 Id。</param>
        /// <param name="roi">检测区域的起点坐标和宽高。</param>
        /// <param name="orientationPosInfo">接收 centerX；头文件未说明坐标基准和无目标时的取值，按原值返回。</param>
        /// <returns>0 成功，负数失败。</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_OrientationPosDetect(int camId, in RoiInfo roi, ref OrientationPosInfo orientationPosInfo);

        /// <summary>清除指定原生相机的检测结果；与关闭相机是不同的 SDK 操作。</summary>
        /// <param name="camId">需要清除检测结果的原生相机编号，不是配置中的业务 Id。</param>
        /// <returns>0 成功，负数失败。</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_ClearDetectResultInfo(int camId);
    }
}
