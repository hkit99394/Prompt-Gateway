using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ControlPlane.Api;

public static class ControlPlaneApiApplicationBuilderExtensions
{
    public static WebApplication MapControlPlaneHttpApi(this WebApplication app)
    {
        var hostOptions = app.Services.GetRequiredService<ControlPlaneApiHostOptions>();

        if (hostOptions.EnableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live")
        });
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = new
                {
                    status = report.Status.ToString(),
                    entries = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        exception = e.Value.Exception?.Message
                    })
                };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(result));
            }
        });

        return app;
    }
}
