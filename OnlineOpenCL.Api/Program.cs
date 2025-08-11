
using OnlineOpenCL.Core;
using OnlineOpenCL.OpenCl;

namespace OnlineOpenCL.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var apiConfig = new ApiConfig();
            builder.Services.AddSingleton(apiConfig);

			builder.Services.AddSingleton<AudioCollection>();
			builder.Services.AddSingleton<ImageCollection>();
			builder.Services.AddSingleton<OpenClService>(sp =>
				new OpenClService(-2)
			);

			// CORS policy
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("BlazorCors", policy =>
				{
					policy.WithOrigins("https://www.oocl.work:7172")
						  .AllowAnyHeader()
						  .AllowAnyMethod();
				});
			});

			builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

			app.UseStaticFiles();

            app.UseAuthorization();

			app.UseCors("BlazorCors");

			app.MapControllers();

            app.Run();
        }
    }

	public class ApiConfig
	{
		public string ServerName { get; set; } = string.Empty;
		public string ServerProtocol { get; set; } = string.Empty;
		public int ServerPort { get; set; } = 0;
		public string ServerUrl { get; set; } = string.Empty;
		public string FQDN { get; set; } = string.Empty;
		public string FQDN_fallback { get; set; } = string.Empty;
		public string ServerVersion { get; set; } = string.Empty;
		public string ServerDescription { get; set; } = string.Empty;
		public int InitializeDeviceId { get; set; } = -1;
		public string DefaultDeviceName { get; set; } = string.Empty;
	}
}
