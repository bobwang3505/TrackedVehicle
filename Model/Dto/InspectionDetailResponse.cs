namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 巡检记录及其视频文件详情。
/// </summary>
public sealed class InspectionDetailResponse
{
    /// <summary>巡检记录。</summary>
    public required InspectionRecord Inspection { get; init; }

    /// <summary>未被软删除的视频文件列表。</summary>
    public required IReadOnlyList<InspectionVideoFile> VideoFiles { get; init; }

    /// <summary>根据文件表实时计算的视频切片数量。</summary>
    public int VideoFileCount => VideoFiles.Count;
}
