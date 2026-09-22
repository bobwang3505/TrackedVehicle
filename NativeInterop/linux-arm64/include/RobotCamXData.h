//
// Created by nvidia on 2026/9/20.
//

#ifndef ROBOTCAMX_ROBOTCAMXDATA_H
#define ROBOTCAMX_ROBOTCAMXDATA_H

extern "C" {
#include <libavformat/avformat.h>
}

namespace robot {
#define MAX_COUNT 10

    struct CamCfg {
        /** 地址 **/
        char chDev[200];
        /** 别名 **/
        char chAlias[200];
    };

    struct MediaInfo {
        /** path **/
        char chPath[200];
    };


    struct RoiInfo {
        int nX;
        int nY;
        int nWidth;
        int nHeight;
    };

    struct LaneDetectInfo {
        int nBoxX;
        int nBoxY;
        int nBoxWidth;
        int nBoxHeight;
        int valid;
    };

    struct LaneDetectGroup {
        LaneDetectInfo infos[MAX_COUNT]{};
        int count = 0;
    };

    /** status 1: 连接成功 - 1: 连接失败*/
    typedef void (*AvStatusFunc)(int id, int status);

    typedef void (*AvFrameIndexFunc)(int id, int64_t frmIdx);

    typedef void (*AvMediaFinishFunc)(int id, const char *fileName, int64_t startTime, int64_t endTime);

    typedef void (*AvStreamFunc)(int id, AVPacket *packet, int64_t frmIdx, int type, int key);
}

#endif //ROBOTCAMX_ROBOTCAMXDATA_H
