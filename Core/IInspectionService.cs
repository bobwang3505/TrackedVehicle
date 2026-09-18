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

    /// <summary>结束指定巡检。</summary>
    Task<InspectionRecord?> FinishAsync(long id, DateTime? endTime);

    /// <summary>为指定巡检登记视频文件。</summary>
    Task<InspectionVideoFile?> AddVideoFileAsync(
        long inspectionRecordId,
        CreateInspectionVideoFileRequest request);

    /// <summary>查询巡检详情。</summary>
    Task<InspectionDetailResponse?> GetAsync(long id);

    /// <summary>查询全部未删除巡检记录。</summary>
    Task<List<InspectionRecord>> GetAllAsync();
}
