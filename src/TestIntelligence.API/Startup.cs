using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestIntelligence.Core.Discovery;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

namespace TestIntelligence.API
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
            // Add services to the container
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { 
                    Title = "TestIntelligence API", 
                    Version = "v1",
                    Description = "RESTful API for AI agent integration with intelligent test selection and analysis"
                });
            });

            // Register TestIntelligence services
            services.AddScoped<ITestSelectionEngine, TestSelectionEngine>();
            services.AddScoped<ITestDiscovery, NUnitTestDiscovery>();
            services.AddScoped<IRoslynAnalyzer, RoslynAnalyzer>();
            services.AddScoped<IGitDiffParser, GitDiffParser>();
            services.AddScoped<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();

            // Add CORS for AI agent integration
            services.AddCors(options =>
            {
                options.AddPolicy("AIAgentPolicy", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Configure the HTTP request pipeline
            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AIAgentPolicy");
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}