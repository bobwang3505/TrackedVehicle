using System.Runtime.InteropServices;

namespace TrackedVehicle
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
    public struct LaneDetectInfo
    {
        public int nBoxX;
        public int nBoxY;
        public int nBoxWidth;
        public int nBoxHeight;
        public int valid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LaneDetectGroup
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeMethods.MAX_COUNT)]
        public LaneDetectInfo[] infos;

        public int count;
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

        /// <summary>开始录像，duration 为分段秒数，返回非负录像 ID，负数失败；recId 为输出编号。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_StartRealTimeRecord(int camId, ref MediaInfo mediaInfo,
            int duration, AvMediaFinishFunc finishFunc, ref int recId);

        /// <summary>停止录像，0 成功，负数失败。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_StopRealTimeRecord(int recId);

        /// <summary>初始化模型，0 成功，负数失败。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_InitModel([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath);

        /// <summary>轨道检测，0 成功，负数失败。调用前将 infos 初始化为长度 MAX_COUNT 的数组。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_LaneDetect(int camId, in RoiInfo roi, ref LaneDetectGroup laneDetGrp);

        /// <summary>清除检测结果，0 成功，负数失败。</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int RobotX_ClearDetectResultInfo(int camId);
    }
}
