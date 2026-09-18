//
// Created by nvidia on 2026/9/11.
//

#ifndef DEMO_DEMO_API_H
#define DEMO_DEMO_API_H

#include "demo_data.h"

#if defined(_WIN32)
#define Demo_API __declspec(dllexport)
#elif defined(__linux__)
#define Demo_API __attribute__((visibility("default")))
#endif

/**
 * 打开相机
 * return 0 on success, a negative on failure
 */
extern "C" Demo_API int Demo_OpenCam(CamCfg* cfg, AvFrameIndexFunc frmIdxFunc, AvStreamFunc avStreamFunc,
                                       AvStatusFunc statusFunc,
                                       int& camId);

/**
 * 关闭相机
 * return 0 on success, a negative on failure
 */
extern "C" Demo_API int Demo_CloseCam(int camId);


/**
 * 开启录像
 * camId: camera id
 * mediaInfo: 视频信息
 * duration: 录制间隔时长(sec)
 * finishFunc: 录制完成回调
 * recId: id
 * return >=0 record_id, a negative on failure
 */
extern "C" Demo_API int Demo_StartRealTimeRecord(int camId, MediaInfo* mediaInfo, int duration,
                                                   AvMediaFinishFunc finishFunc, int& recId);

/**
 * 停止录像
 * recId: 开启录制id
 * return 0 on success, a negative on failure
 */
extern "C" Demo_API int Demo_StopRealTimeRecord(int recId);


extern "C" Demo_API int Demo_Detect(int camId, const RoiInfo* roi, DetGroupInfo* detGrpInfo);



#endif //DEMO_DEMO_API_H
