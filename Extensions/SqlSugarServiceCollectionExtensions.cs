using Serilog;
using SqlSugar;
using TrackedVehicle.Infrastructure.IdGeneration;

namespace TrackedVehicle.Extensions;

/// <summary>
/// SqlSugar 与 SQLite 注册扩展。
/// </summary>
public static class SqlSugarServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SQLite 和 SqlSugar，主键由已注册的 IIdGenerator 生成。
    /// </summary>
    public static IServiceCollection AddSqlSugarSqlite(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddSingleton<ISqlSugarClient>(serviceProvider =>
        {
            var relativePath = configuration["Database:SQLitePath"]
                ?? throw new InvalidOperationException("未配置 Database:SQLitePath。");
            var databasePath = Path.GetFullPath(relativePath, environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

            var idGenerator = serviceProvider.GetRequiredService<IIdGenerator>();
            var database = new SqlSugarScope(
                new ConnectionConfig
                {
                    ConfigId = "TrackedVehicleSQLite",
                    DbType = SqlSugar.DbType.Sqlite,
                    ConnectionString = $"Data Source={databasePath}",
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute
                },
                client =>
                {
                    client.Aop.DataExecuting = (oldValue, entityInfo) =>
                    {
                        if (entityInfo.OperationType == DataFilterType.InsertByObject
                            && entityInfo.PropertyName == "Id"
                            && Convert.ToInt64(oldValue ?? 0L) == 0L)
                        {
                            entityInfo.SetValue(idGenerator.Create());
                        }
                    };

                    client.Aop.OnLogExecuting = (sql, parameters) =>
                    {
                        var executableSql = UtilMethods.GetSqlString(
                            SqlSugar.DbType.Sqlite,
                            sql,
                            parameters);
                        var originalColor = Console.ForegroundColor;

                        try
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("========== SqlSugar SQL ==========");
                            Console.WriteLine(executableSql);
                            Console.WriteLine("==================================");
                        }
                        finally
                        {
                            Console.ForegroundColor = originalColor;
                        }

                        Log.Information("SqlSugar SQL:{NewLine}{Sql}", Environment.NewLine, executableSql);
                    };
                });

            return database;
        });

        return services;
    }
}
