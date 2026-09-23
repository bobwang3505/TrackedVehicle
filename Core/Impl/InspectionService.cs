using SqlSugar;
using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Core.Impl;

/// <summary>
/// 巡检记录业务服务实现。
/// </summary>
public sealed class InspectionService(ISqlSugarClient database) : IInspectionService
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

        var finishTime = endTime ?? DateTime.Now;
        if (finishTime < record.StartTime)
        {
            throw new ArgumentException("巡检结束时间不能早于开始时间。", nameof(endTime));
        }

        record.EndTime = finishTime;
        await database.Updateable(record)
            .UpdateColumns(item => new { item.EndTime })
            .ExecuteCommandAsync();

        return record;
    }

    public async Task<InspectionVideoFile?> AddVideoFileAsync(
        long inspectionRecordId,
        CreateInspectionVideoFileRequest request)
    {
        var inspectionExists = await database.Queryable<InspectionRecord>()
            .AnyAsync(item => item.Id == inspectionRecordId && !item.IsDelete);

        if (!inspectionExists)
        {
            return null;
        }

        var videoFile = new InspectionVideoFile
        {
            InspectionRecordId = inspectionRecordId,
            FilePath = request.FilePath.Trim(),
            IsUploaded = false,
            CreateTime = request.CreateTime ?? DateTime.Now,
            IsDelete = false
        };

        await database.Insertable(videoFile).ExecuteCommandAsync();
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
