using System.Diagnostics;

public class Handler
{
    private readonly DropboxClient _dropbox;
    private readonly string _localRoot;
    private readonly string _serverJar;
    private readonly string _hostId;
    private readonly int _backupKeep;

    private const string HandlerLogPath = "/handler_log.json";
    private const string LatestZipPath = "/world_latest.zip";
    private const string BackupsFolder = "/backups";

    public Handler(DropboxClient dropbox, string localRoot, string serverJar, int backupKeep)
    {
        _dropbox = dropbox;
        _localRoot = localRoot;
        _serverJar = serverJar;
        _backupKeep = Math.Max(1, backupKeep);
        _hostId = $"{Environment.MachineName}_{Environment.UserName}";

        Directory.CreateDirectory(localRoot);
        Directory.CreateDirectory(Path.Combine(localRoot, "world"));
        Directory.CreateDirectory(Path.Combine(localRoot, "backups"));
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"Handler starting on {_hostId}...");

        try
        {
            var localLog = Path.Combine(_localRoot, "handler_log.json");
            var tmp = Path.Combine(_localRoot, "_tmp_log.json");
            var someoneActive = false;

            if (await _dropbox.FileExistsAsync(HandlerLogPath))
            {
                var ok = await _dropbox.DownloadFileAsync(HandlerLogPath, tmp);
                if (ok && File.Exists(tmp))
                {
                    var json = File.ReadAllText(tmp);
                    var log = HandlerLog.FromJson(json);
                    if (log != null && !string.IsNullOrEmpty(log.ActiveHost))
                    {
                        if ((DateTimeOffset.UtcNow - log.LastUpdated).TotalMinutes < 5 &&
                            log.ActiveHost != _hostId)
                        {
                            Console.WriteLine($"Another host active: {log.ActiveHost}");
                            someoneActive = true;
                        }
                    }
                }
            }

            if (someoneActive)
            {
                Console.WriteLine("Another handler is active — exiting.");
                return;
            }

            if (await _dropbox.FileExistsAsync(LatestZipPath))
            {
                var localZip = Path.Combine(_localRoot, "world_latest.zip");
                await _dropbox.DownloadFileAsync(LatestZipPath, localZip);
                Utils.ExtractZip(localZip, Path.Combine(_localRoot, "world"));
            }

            var myLog = HandlerLog.Create(_hostId);
            File.WriteAllText(localLog, myLog.ToJson());
            await _dropbox.UploadFileAsync(HandlerLogPath, localLog);

            var serverPath = Path.Combine(_localRoot, _serverJar);
            if (File.Exists(serverPath))
                await LaunchServerAsync(serverPath);
            else
                Console.WriteLine($"Server jar missing: {serverPath}");

            await UploadWorldBackupAsync();

            var clear = new HandlerLog { ActiveHost = null, LastUpdated = DateTimeOffset.UtcNow };
            var tmpClear = Path.Combine(_localRoot, "_clear_log.json");
            File.WriteAllText(tmpClear, clear.ToJson());
            await _dropbox.UploadFileAsync(HandlerLogPath, tmpClear);

            Console.WriteLine("Handler completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Handler error: {ex.Message}");
        }
    }

    private async Task LaunchServerAsync(string serverJarPath)
    {
        try
        {
            Console.WriteLine("Launching Minecraft server...");
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{serverJarPath}\" nogui",
                WorkingDirectory = _localRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("ERR: " + e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();
            Console.WriteLine($"Server exited with code {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server launch error: {ex.Message}");
        }
    }

    private async Task UploadWorldBackupAsync()
    {
        try
        {
            var world = Path.Combine(_localRoot, "world");
            var latestZip = Path.Combine(_localRoot, "world_latest.zip");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var backupLocal = Path.Combine(_localRoot, "backups", $"world_{timestamp}.zip");

            if (!Directory.Exists(world))
            {
                Console.WriteLine("[Handler] No world folder found — skipping backup.");
                return;
            }

            if (Utils.ZipFolder(world, latestZip))
            {
                Console.WriteLine($"Zipped {world} → {latestZip}");
                await _dropbox.UploadFileAsync(LatestZipPath, latestZip);
                await _dropbox.UploadFileAsync($"{BackupsFolder}/world_{timestamp}.zip", latestZip);
                File.Copy(latestZip, backupLocal, true);
                Utils.CleanOldBackupsLocal(Path.Combine(_localRoot, "backups"), _backupKeep);
                Console.WriteLine("Backup upload complete.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Handler] Backup upload error: {ex.Message}");
        }
    }

    public async Task SafeShutdownAsync()
    {
        try
        {
            Console.WriteLine("[Handler] Safe shutdown triggered — saving and uploading world...");
            var world = Path.Combine(_localRoot, "world");
            var latestZip = Path.Combine(_localRoot, "world_latest.zip");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var backupLocal = Path.Combine(_localRoot, "backups", $"world_{timestamp}.zip");

            if (Directory.Exists(world))
            {
                Utils.ZipFolder(world, latestZip);
                await _dropbox.UploadFileAsync("/world_latest.zip", latestZip);
                await _dropbox.UploadFileAsync($"/backups/world_{timestamp}.zip", latestZip);
                File.Copy(latestZip, backupLocal, true);
                Console.WriteLine("[Handler] Upload complete.");
            }

            var clear = new HandlerLog { ActiveHost = null, LastUpdated = DateTimeOffset.UtcNow };
            var tmpClear = Path.Combine(_localRoot, "_clear_log.json");
            File.WriteAllText(tmpClear, clear.ToJson());
            await _dropbox.UploadFileAsync("/handler_log.json", tmpClear);

            Console.WriteLine("[Handler] Safe shutdown finished.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Handler] Safe shutdown error: {ex.Message}");
        }
    }

}
