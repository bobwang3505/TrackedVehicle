using SqlSugar;
using TrackedVehicle.Model;

namespace TrackedVehicle.Extensions;

/// <summary>
/// 数据库初始化扩展。
/// </summary>
public static class DatabaseInitializerExtensions
{
    /// <summary>
    /// 创建 SQLite 数据库并按实体初始化表结构。
    /// </summary>
    public static WebApplication InitializeDatabase(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<ISqlSugarClient>();

        database.DbMaintenance.CreateDatabase();
        database.CodeFirst.InitTables<InspectionRecord, InspectionVideoFile>();

        return app;
    }
}
