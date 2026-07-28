using System.Text.Json;
using AdventurePacks.Api.Infrastructure;

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
                    TooManyRequestsException => StatusCodes.Status429TooManyRequests,
                    KeyNotFoundException => StatusCodes.Status404NotFound,
                    InvalidOperationException => StatusCodes.Status400BadRequest,
                    ArgumentException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                };

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var retryAfterSeconds = (exception as TooManyRequestsException)?.RetryAfterSeconds;
                if (retryAfterSeconds is { } retryAfter)
                {
                    context.Response.Headers.RetryAfter = retryAfter.ToString();
                }

                var response = JsonSerializer.Serialize(new
                {
                    message = exception?.Message ?? "An unexpected error occurred.",
                    retryAfterSeconds
                });
                await context.Response.WriteAsync(response);
            });
        });

        return app;
    }
}
