using Microsoft.AspNetCore.Mvc;
using TrackedVehicle.Core;
using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Controllers;

/// <summary>
/// C++ 摄像和算法动态库测试接口。
/// </summary>
[ApiController]
[Route("api/native-sdk")]
public sealed class NativeSdkController(INativeAlgorithmService nativeAlgorithmService) : ControllerBase
{
    /// <summary>
    /// 查看当前平台、架构和动态库文件状态，不会加载动态库。
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(NativeSdkStatus), StatusCodes.Status200OK)]
    public ActionResult<NativeSdkStatus> GetStatus()
    {
        return Ok(nativeAlgorithmService.GetStatus());
    }

    /// <summary>
    /// 调用原生动态库打开摄像头。
    /// </summary>
    [HttpPost("cameras/open")]
    [ProducesResponseType(typeof(OpenCameraResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<OpenCameraResult> OpenCamera(OpenCameraRequest request)
    {
        return Execute(() => nativeAlgorithmService.OpenCamera(
            request.Device,
            request.Alias,
            request.ParentCameraId));
    }

    /// <summary>
    /// 调用原生动态库关闭摄像头。
    /// </summary>
    /// <param name="cameraId">打开摄像头时返回的 ID。</param>
    [HttpPost("cameras/{cameraId:int}/close")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<int> CloseCamera(int cameraId)
    {
        return Execute(() => nativeAlgorithmService.CloseCamera(cameraId));
    }

    /// <summary>
    /// 调用原生算法检测指定图像区域。
    /// </summary>
    /// <param name="cameraId">打开摄像头时返回的 ID。</param>
    /// <param name="request">检测区域坐标和尺寸。</param>
    [HttpPost("cameras/{cameraId:int}/detect")]
    [ProducesResponseType(typeof(NativeDetectionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<NativeDetectionResult> Detect(int cameraId, DetectRequest request)
    {
        return Execute(() => nativeAlgorithmService.Detect(
            cameraId,
            new DetectionRegion(request.X, request.Y, request.Width, request.Height)));
    }

    /// <summary>
    /// 调用原生动态库开始录像。
    /// </summary>
    /// <param name="cameraId">打开摄像头时返回的 ID。</param>
    /// <param name="request">保存路径和录像切片时长。</param>
    [HttpPost("cameras/{cameraId:int}/recordings/start")]
    [ProducesResponseType(typeof(StartRecordingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<StartRecordingResult> StartRecording(
        int cameraId,
        StartRecordingRequest request)
    {
        return Execute(() => nativeAlgorithmService.StartRecording(
            cameraId,
            request.FilePath,
            request.DurationSeconds));
    }

    /// <summary>
    /// 调用原生动态库停止录像。
    /// </summary>
    /// <param name="recordingId">开始录像时返回的 ID。</param>
    [HttpPost("recordings/{recordingId:int}/stop")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<int> StopRecording(int recordingId)
    {
        return Execute(() => nativeAlgorithmService.StopRecording(recordingId));
    }

    private ActionResult<T> Execute<T>(Func<T> action)
    {
        try
        {
            return Ok(action());
        }
        catch (Exception exception) when (
            exception is PlatformNotSupportedException
                or DllNotFoundException
                or BadImageFormatException
                or EntryPointNotFoundException
                or FileNotFoundException
                or InvalidOperationException)
        {
            return Problem(
                title: "原生动态库当前不可用",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
