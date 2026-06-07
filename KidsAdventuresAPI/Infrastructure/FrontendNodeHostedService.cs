using System.Diagnostics;
using AdventurePacks.Api.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AdventurePacks.Api.Infrastructure;

public sealed class FrontendNodeHostedService(
    IWebHostEnvironment environment,
    IOptions<FrontendHostingOptions> options,
    ILogger<FrontendNodeHostedService> logger) : IHostedService
{
    private Process? _nodeProcess;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.EnableHostedNode)
        {
            return Task.CompletedTask;
        }

        var outputDir = Path.Combine(environment.ContentRootPath, settings.OutputRelativePath);
        var entry = Path.Combine(outputDir, "server", "index.mjs");
        if (!File.Exists(entry))
        {
            logger.LogWarning(
                "Frontend hosted Node is enabled but {Entry} was not found. API-only mode.",
                entry);
            return Task.CompletedTask;
        }

        var nodeExecutable = ResolveNodeExecutable();
        if (nodeExecutable is null)
        {
            logger.LogWarning(
                "Frontend hosted Node is enabled but the node executable was not found. API-only mode.");
            return Task.CompletedTask;
        }

        var port = settings.NodePort;
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            Arguments = "server/index.mjs",
            WorkingDirectory = outputDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["PORT"] = port.ToString();
        startInfo.Environment["NITRO_PORT"] = port.ToString();
        startInfo.Environment["NODE_ENV"] = "production";

        try
        {
            _nodeProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not start frontend Node process in {OutputDir}. API-only mode.",
                outputDir);
            return Task.CompletedTask;
        }

        if (_nodeProcess is null)
        {
            logger.LogWarning("Failed to start frontend Node process. API-only mode.");
            return Task.CompletedTask;
        }

        _nodeProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger.LogInformation("frontend-node: {Line}", e.Data);
            }
        };
        _nodeProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger.LogWarning("frontend-node: {Line}", e.Data);
            }
        };
        _nodeProcess.BeginOutputReadLine();
        _nodeProcess.BeginErrorReadLine();

        logger.LogInformation("Started frontend Node SSR on port {Port}", port);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_nodeProcess is { HasExited: false })
        {
            try
            {
                _nodeProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not stop frontend Node process.");
            }
        }

        _nodeProcess?.Dispose();
        return Task.CompletedTask;
    }

    private static string? ResolveNodeExecutable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var dir in pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "node.exe" : "node");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string[] fallbacks =
        [
            "/usr/local/bin/node",
            "/usr/bin/node",
            @"C:\Program Files\nodejs\node.exe"
        ];

        foreach (var candidate in fallbacks)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
