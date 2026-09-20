//
// Created by nvidia on 2026/9/20.
//

#ifndef ROBOTCAMX_ROBOTCAMXAPI_H
#define ROBOTCAMX_ROBOTCAMXAPI_H


#define API_INTERFACE 1


#if API_INTERFACE

#include "RobotCamXData.h"

#if defined(_WIN32)
#define ROBOT_CAM_X_API __declspec(dllexport)
#elif defined(__linux__)
#define ROBOT_CAM_X_API __attribute__((visibility("default")))
#endif

using namespace robot;

#pragma region Camera

/**
 * 打开相机
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_OpenCam(CamCfg *cfg, AvFrameIndexFunc frmIdxFunc, AvStatusFunc statusFunc,
                                              int &camId);

/**
 * 关闭相机
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_CloseCam(int camId);

/**
 * 开启录像
 * camId: camera id
 * mediaInfo: 视频信息
 * duration: 录制间隔时长(sec)
 * finishFunc: 录制完成回调
 * recId: id
 * return >=0 record_id, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_StartRealTimeRecord(int camId, MediaInfo* mediaInfo, int duration,
                                                   AvMediaFinishFunc finishFunc, int& recId);

/**
 * 停止录像
 * recId: 开启录制id
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_StopRealTimeRecord(int recId);


#pragma endregion

#pragma region AI Detect

/**
 * 初始化模型
 * modelPath: 模型路径
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_InitModel(const char* modelPath);

/**
 * 轨道检测
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_LaneDetect(int camId, const RoiInfo* roi, LaneDetectGroup* laneDetGrp);

/**
 * 清除检测结果
 * return 0 on success, a negative on failure
 */
extern "C" ROBOT_CAM_X_API int RobotX_ClearDetectResultInfo(int camId);

#pragma endregion

#endif


#endif //ROBOTCAMX_ROBOTCAMXAPI_H
