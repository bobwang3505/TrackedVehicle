using SqlSugar;
using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Core.Impl;

/// <summary>
/// 巡检记录业务服务实现。
/// </summary>
public sealed class InspectionService(ISqlSugarClient database, VideoUploadQueue uploadQueue) : IInspectionService
{
    public async Task<InspectionRecord> StartAsync(DateTime? startTime)
    {
        var record = new InspectionRecord
        {
            StartTime = startTime ?? DateTime.Now,
            CreateTime = DateTime.Now,
            IsDelete = false
        };

        await database.Insertable(record).ExecuteCommandAsync();
        return record;
    }

    public async Task<InspectionRecord?> FinishAsync(long id, DateTime? endTime)
    {
        var record = await database.Queryable<InspectionRecord>()
            .Where(item => item.Id == id && !item.IsDelete)
            .FirstAsync();

        if (record is null)
        {
            return null;
        }

        // 重复结束同一趟巡检时保留首次结束时间，避免重试把结束时间向后推。
        if (record.EndTime.HasValue) return record;

        var finishTime = endTime ?? DateTime.Now;
        if (finishTime < record.StartTime)
        {
            throw new ArgumentException("巡检结束时间不能早于开始时间。", nameof(endTime));
        }

        record.EndTime = finishTime;
        await database.Updateable(record)
            .UpdateColumns(item => new { item.EndTime })
            .Where(item => item.Id == id && !item.IsDelete && item.EndTime == null)
            .ExecuteCommandAsync();

        // 并发结束时以数据库中首先写入的结束时间为准。
        return await database.Queryable<InspectionRecord>()
            .Where(item => item.Id == id && !item.IsDelete).FirstAsync();
    }

    public async Task<InspectionVideoFile?> AddVideoFileAsync(
        long inspectionRecordId,
        CreateInspectionVideoFileRequest request)
    {
        // 后台登记也会直接调用服务，因此不能只依赖 HTTP 请求参数校验。
        if (string.IsNullOrWhiteSpace(request.CameraId) || request.CameraId.Length > 100)
            throw new ArgumentException("必须提供相机业务 Id，长度不能超过 100。", nameof(request));
        if (!request.StartTime.HasValue || !request.EndTime.HasValue || request.EndTime < request.StartTime)
            throw new ArgumentException("必须提供切片录像起止时间，结束时间不能早于开始时间。", nameof(request));

        var inspectionExists = await database.Queryable<InspectionRecord>()
            .AnyAsync(item => item.Id == inspectionRecordId && !item.IsDelete);

        if (!inspectionExists)
        {
            return null;
        }

        var videoFile = new InspectionVideoFile
        {
            InspectionRecordId = inspectionRecordId,
            CameraId = request.CameraId.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            FilePath = request.FilePath.Trim(),
            IsUploaded = false,
            CreateTime = request.CreateTime ?? DateTime.Now,
            IsDelete = false
        };

        await database.Insertable(videoFile).ExecuteCommandAsync();
        // 先持久化再通知上传；队列满或上传关闭时记录仍在库中，启用后的扫描会补入。
        uploadQueue.TryEnqueue(videoFile.Id);
        return videoFile;
    }

    public async Task<InspectionDetailResponse?> GetAsync(long id)
    {
        var inspection = await database.Queryable<InspectionRecord>()
            .Where(item => item.Id == id && !item.IsDelete)
            .FirstAsync();

        if (inspection is null)
        {
            return null;
        }

        var videoFiles = await database.Queryable<InspectionVideoFile>()
            .Where(item => item.InspectionRecordId == id && !item.IsDelete)
            .OrderBy(item => item.CreateTime)
            .ToListAsync();

        return new InspectionDetailResponse
        {
            Inspection = inspection,
            VideoFiles = videoFiles
        };
    }

    public Task<List<InspectionRecord>> GetAllAsync()
    {
        return database.Queryable<InspectionRecord>()
            .Where(item => !item.IsDelete)
            .OrderByDescending(item => item.StartTime)
            .ToListAsync();
    }
}
