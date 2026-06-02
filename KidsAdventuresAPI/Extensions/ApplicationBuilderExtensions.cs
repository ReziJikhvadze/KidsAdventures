using System.Text.Json;

namespace AdventurePacks.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var exception = feature?.Error;

                var statusCode = exception switch
                {
                    UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                    InvalidOperationException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                };

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var response = JsonSerializer.Serialize(new
                {
                    message = exception?.Message ?? "An unexpected error occurred."
                });
                await context.Response.WriteAsync(response);
            });
        });

        return app;
    }
}
