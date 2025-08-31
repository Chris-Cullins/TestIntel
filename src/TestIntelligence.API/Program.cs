using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Discovery;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "TestIntelligence API", 
        Version = "v1",
        Description = "RESTful API for AI agent integration with intelligent test selection and analysis"
    });
});

// Register TestIntelligence services
builder.Services.AddScoped<ITestSelectionEngine, TestSelectionEngine>();
builder.Services.AddScoped<ITestDiscovery, NUnitTestDiscovery>();
builder.Services.AddScoped<IRoslynAnalyzer, RoslynAnalyzer>();
builder.Services.AddScoped<IGitDiffParser, GitDiffParser>();
builder.Services.AddScoped<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();

// Add CORS for AI agent integration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AIAgentPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AIAgentPolicy");
app.UseAuthorization();
app.MapControllers();

app.Run();