using Microsoft.AspNetCore.Mvc;
using TrackedVehicle.Core;
using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Controllers;

/// <summary>
/// 巡检记录与视频切片管理接口。
/// </summary>
[ApiController]
[Route("api/inspections")]
public sealed class InspectionsController(IInspectionService inspectionService) : ControllerBase
{
    /// <summary>
    /// 查询全部未删除的巡检记录。
    /// </summary>
    /// <returns>按照开始时间倒序排列的巡检记录。</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<InspectionRecord>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<InspectionRecord>>> GetAll()
    {
        return Ok(await inspectionService.GetAllAsync());
    }

    /// <summary>
    /// 查询一趟巡检及其全部视频切片。
    /// </summary>
    /// <param name="id">巡检记录的雪花 ID。</param>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(InspectionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InspectionDetailResponse>> Get(long id)
    {
        var result = await inspectionService.GetAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// 开始一趟新的巡检。
    /// </summary>
    /// <remarks>主键由服务端雪花算法自动生成，不需要调用方传入。</remarks>
    [HttpPost("start")]
    [ProducesResponseType(typeof(InspectionRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<InspectionRecord>> Start(StartInspectionRequest request)
    {
        var record = await inspectionService.StartAsync(request.StartTime);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, record);
    }

    /// <summary>
    /// 结束一趟巡检。
    /// </summary>
    /// <param name="id">巡检记录的雪花 ID。</param>
    /// <param name="request">可选的巡检结束时间。</param>
    [HttpPut("{id:long}/finish")]
    [ProducesResponseType(typeof(InspectionRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InspectionRecord>> Finish(long id, FinishInspectionRequest request)
    {
        try
        {
            var record = await inspectionService.FinishAsync(id, request.EndTime);
            return record is null ? NotFound() : Ok(record);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// 为一趟巡检登记一个本地视频切片文件。
    /// </summary>
    /// <param name="id">巡检记录的雪花 ID。</param>
    /// <param name="request">本地视频切片文件信息。</param>
    [HttpPost("{id:long}/videos")]
    [ProducesResponseType(typeof(InspectionVideoFile), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InspectionVideoFile>> AddVideoFile(
        long id,
        CreateInspectionVideoFileRequest request)
    {
        var videoFile = await inspectionService.AddVideoFileAsync(id, request);
        return videoFile is null
            ? NotFound()
            : StatusCode(StatusCodes.Status201Created, videoFile);
    }
}
