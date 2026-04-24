using System.Diagnostics;

namespace InstagramEmbed.Application.Services
{
    public sealed class SnapSaveProcessService : IHostedService, IDisposable
    {
        private readonly ILogger<SnapSaveProcessService> _logger;
        private readonly IConfiguration _config;
        private Process? _process;

        public SnapSaveProcessService(ILogger<SnapSaveProcessService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var port = _config.GetValue<int>("SnapSave:Port", 3200);
            var workDir = _config.GetValue<string>("SnapSave:WorkDir", "snapsave")!;

            // Resolve relative to the app's content root
            if (!Path.IsPathRooted(workDir))
                workDir = Path.Combine(AppContext.BaseDirectory, workDir);

            if (!Directory.Exists(workDir))
            {
                _logger.LogWarning("snapsave workdir '{Dir}' not found – skipping Node startup", workDir);
                return Task.CompletedTask;
            }

            var nodeExe = FindNodeExecutable();
            if (nodeExe == null)
            {
                _logger.LogError("Node.js not found on PATH – snapsave service will not start");
                return Task.CompletedTask;
            }

            var psi = new ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = "index.js",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.Environment["SNAPSAVE_PORT"] = port.ToString();

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) _logger.LogInformation("[snapsave] {Line}", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) _logger.LogWarning("[snapsave] {Line}", e.Data);
            };
            _process.Exited += (_, _) =>
                _logger.LogWarning("[snapsave] process exited unexpectedly (code {Code})", _process?.ExitCode);

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logger.LogInformation("snapsave started (pid {Pid}) on port {Port}", _process.Id, port);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_process is { HasExited: false })
            {
                _logger.LogInformation("Stopping snapsave (pid {Pid})…", _process.Id);
                try
                {
                    // Send SIGTERM equivalent on all platforms
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping snapsave process");
                }
            }
        }

        public void Dispose() => _process?.Dispose();

        private static string? FindNodeExecutable()
        {
            foreach (var candidate in new[] { "node", "node.exe" })
            {
                var result = Which(candidate);
                if (result != null) return result;
            }
            return null;
        }

        private static string? Which(string exe)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
            foreach (var dir in paths)
            {
                var full = Path.Combine(dir, exe);
                if (File.Exists(full)) return full;
            }
            return null;
        }
    }

}
