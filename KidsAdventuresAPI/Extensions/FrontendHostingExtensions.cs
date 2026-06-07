using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Infrastructure;
using Yarp.ReverseProxy.Forwarder;

namespace AdventurePacks.Api.Extensions;

public static class FrontendHostingExtensions
{
    private static readonly string[] ApiPathPrefixes =
    [
        "/api",
        "/swagger",
        "/hangfire"
    ];

    public static IServiceCollection AddFrontendHosting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FrontendHostingOptions>(configuration.GetSection(FrontendHostingOptions.SectionName));
        services.AddHttpForwarder();
        services.AddSingleton(_ => new HttpMessageInvoker(new SocketsHttpHandler()));
        services.AddHostedService<FrontendNodeHostedService>();
        return services;
    }

    public static WebApplication UseFrontendHosting(this WebApplication app)
    {
        var options = app.Configuration
            .GetSection(FrontendHostingOptions.SectionName)
            .Get<FrontendHostingOptions>() ?? new FrontendHostingOptions();

        if (!options.EnableHostedNode)
        {
            return app;
        }

        var outputEntry = Path.Combine(app.Environment.ContentRootPath, options.OutputRelativePath, "server", "index.mjs");
        if (!File.Exists(outputEntry))
        {
            return app;
        }

        var destination = $"http://127.0.0.1:{options.NodePort}";
        var transformer = HttpTransformer.Default;
        var requestConfig = new ForwarderRequestConfig();

        app.MapWhen(
            context => ShouldProxyToFrontend(context.Request.Path),
            branch =>
            {
                branch.Run(async context =>
                {
                    var forwarder = context.RequestServices.GetRequiredService<IHttpForwarder>();
                    var httpClient = context.RequestServices.GetRequiredService<HttpMessageInvoker>();
                    var error = await forwarder.SendAsync(
                        context,
                        destination,
                        httpClient,
                        requestConfig,
                        transformer);

                    if (error != ForwarderError.None)
                    {
                        var logger = context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("FrontendProxy");
                        var errorFeature = context.Features.Get<IForwarderErrorFeature>();
                        logger.LogError(
                            errorFeature?.Exception,
                            "Frontend proxy error: {Error}",
                            error);
                        context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    }
                });
            });

        return app;
    }

    private static bool ShouldProxyToFrontend(PathString path)
    {
        if (!path.HasValue)
        {
            return true;
        }

        var value = path.Value!;
        foreach (var prefix in ApiPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
