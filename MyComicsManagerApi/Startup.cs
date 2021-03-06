using System;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyComicsManagerApi.ComputerVision;
using MyComicsManagerApi.Models;
using MyComicsManagerApi.Services;
using MyComicsManagerWeb.Settings;
using Serilog;

namespace MyComicsManagerApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // requires using Microsoft.Extensions.Options
            services.Configure<DatabaseSettings>( Configuration.GetSection(nameof(DatabaseSettings)));
            services.AddSingleton<IDatabaseSettings>(sp => sp.GetRequiredService<IOptions<DatabaseSettings>>().Value);

            services.Configure<LibrairiesSettings>( Configuration.GetSection(nameof(LibrairiesSettings)));
            services.AddSingleton<ILibrairiesSettings>(sp => sp.GetRequiredService<IOptions<LibrairiesSettings>>().Value);

            services.Configure<AzureSettings>(Configuration.GetSection(nameof(AzureSettings)));
            services.AddSingleton<IAzureSettings>(sp => sp.GetRequiredService<IOptions<AzureSettings>>().Value);
            
            services.Configure<NotificationSettings>( Configuration.GetSection(nameof(NotificationSettings)));
            services.AddSingleton<INotificationSettings>(sp => sp.GetRequiredService<IOptions<NotificationSettings>>().Value);
            
            var settings = Configuration.GetSection(nameof(DatabaseSettings)).Get<DatabaseSettings>();
            
            var mongoUrlBuilder = new MongoUrlBuilder(settings.ConnectionString)
            {
                DatabaseName = "hangfire"
            };
            var mongoClient = new MongoClient(mongoUrlBuilder.ToMongoUrl());
            
            services.AddHttpClient<BookService>();

            // Add Hangfire services. Hangfire.AspNetCore nuget required
            services.AddHangfire(configuration => configuration
                .UseSerilogLogProvider()
                .UseColouredConsoleLogProvider()
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMongoStorage(mongoClient, mongoUrlBuilder.DatabaseName, new MongoStorageOptions
                {
                    MigrationOptions = new MongoMigrationOptions
                    {
                        MigrationStrategy = new MigrateMongoMigrationStrategy(),
                        BackupStrategy = new CollectionMongoBackupStrategy()
                    },
                    Prefix = "hangfire",
                    CheckConnection = true
                })
            );
            // Add the processing server as IHostedService
            services.AddHangfireServer(serverOptions =>
            {
                // Par d??faut : Environment.ProcessorCount * 2
                serverOptions.WorkerCount = 1;
            });

            services.AddSingleton<ComicService>();
            services.AddSingleton<LibraryService>();
            services.AddSingleton<ComicFileService>();
            services.AddSingleton<BookService>();
            services.AddSingleton<ComputerVisionService>();
            services.AddSingleton<NotificationService>();

            services.AddControllers().AddNewtonsoftJson(options => options.UseMemberCasing());

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
                // To serve the Swagger UI at the app's root (http://localhost:<port>/), set the RoutePrefix property to an empty string
                c.RoutePrefix = string.Empty;
            });
            
            // hangfire configuration
            var options = new DashboardOptions
            {
                Authorization = new[] { new MyAuthorizationFilter() }
            };
            app.UseHangfireDashboard("/hangfire", options);

            app.UseRouting();

            app.UseSerilogRequestLogging();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            
        }
        
        public class MyAuthorizationFilter : IDashboardAuthorizationFilter
        {
            public bool Authorize(DashboardContext context) => true;
        }
    }
}
