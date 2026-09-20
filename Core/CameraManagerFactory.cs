using TrackedVehicle.Core.Impl;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>每次创建独立的相机对象，并注入它自己的配置和公共依赖。</summary>
public sealed class CameraManagerFactory(IServiceProvider serviceProvider)
{
    public ICameraManager Create(CameraOptions options)
    {
        return ActivatorUtilities.CreateInstance<CameraManager>(serviceProvider, options);
    }
}
