
namespace TrackedVehicle
{
    using Core;
    using Core.Impl;
    using Extensions;

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.AddFileLogging();
            builder.Services.AddSnowflakeIdGeneration(builder.Configuration);
            builder.Services.AddSqlSugarSqlite(builder.Configuration, builder.Environment);
            builder.Services.AddVideoUpload(builder.Configuration);
            builder.Services.AddVehicleControl(builder.Configuration);

            builder.Services.AddControllers();
            builder.Services.AddScoped<IInspectionService, InspectionService>();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
            });

            var app = builder.Build();

            app.InitializeDatabase();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
