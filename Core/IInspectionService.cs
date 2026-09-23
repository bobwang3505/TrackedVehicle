using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Core;

/// <summary>
/// 巡检记录业务服务。
/// </summary>
public interface IInspectionService
{
    /// <summary>开始一次巡检。</summary>
    Task<InspectionRecord> StartAsync(DateTime? startTime);

    /// <summary>结束指定巡检；重复调用保留首次结束时间。录像停止由调用方负责。</summary>
    Task<InspectionRecord?> FinishAsync(long id, DateTime? endTime);

    /// <summary>为指定巡检登记已录制完成的视频文件，存库成功后通知上传队列。</summary>
    Task<InspectionVideoFile?> AddVideoFileAsync(
        long inspectionRecordId,
        CreateInspectionVideoFileRequest request);

    /// <summary>查询巡检详情。</summary>
    Task<InspectionDetailResponse?> GetAsync(long id);

    /// <summary>查询全部未删除巡检记录。</summary>
    Task<List<InspectionRecord>> GetAllAsync();
}
