using Microsoft.AspNetCore.Mvc;
using SmartRecipeBox.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<McpExplainOrchestrator>();

var app = builder.Build();

app.UseDefaultFiles(); 
app.UseStaticFiles();  

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Error handling middleware
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var error = feature?.Error;

        var response = new
        {
            error = "שגיאה בטעינת המערכת",
            message = app.Environment.IsDevelopment() ? error?.Message : "אנא בדוק את הגדרות המפתחות"
        };

        await context.Response.WriteAsJsonAsync(response);
    });
});

app.Run();