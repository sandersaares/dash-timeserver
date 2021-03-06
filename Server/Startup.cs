using Autofac;
using Koek;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Prometheus.Experimental;

namespace DashTimeserver.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Allow everything in CORS because players tend to need this.
            app.UseCors(builder =>
            {
                builder.AllowAnyOrigin();
                builder.AllowAnyHeader();
                builder.AllowAnyMethod();
            });

            app.UseRouting();
            app.UseHttpMetrics();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapMetrics();
            });

            LocalTimeMetrics.Register();

            var trueTimeMetrics = app.ApplicationServices.GetRequiredService<TrueTimeMetrics>();
            trueTimeMetrics.Register();
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            //builder.RegisterType<LocalTimeSource>().As<ITimeSource>().SingleInstance();
            builder.RegisterType<NtpTimeSource>().As<ITimeSource>().SingleInstance();

            builder.RegisterType<TrueTimeMetrics>().SingleInstance();
        }
    }
}
