using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Minecraft Co-Host Handler (NET 8) ===");

        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var dropboxAccessToken = config["Dropbox:AccessToken"];
            var dropboxRefreshToken = config["Dropbox:RefreshToken"];
            var dropboxAppKey = config["Dropbox:AppKey"];
            var dropboxAppSecret = config["Dropbox:AppSecret"];
            var localFolder = config["LocalServerFolder"] ?? "C:\\Minecraft_Server";
            var serverJar = config["ServerJar"] ?? "server.jar";
            var keepStr = config["BackupRotationKeep"] ?? "7";
            if (!int.TryParse(keepStr, out var backupKeep)) backupKeep = 7;

            if (string.IsNullOrWhiteSpace(dropboxAccessToken))
            {
                Console.WriteLine("ERROR: Dropbox AccessToken missing in appsettings.json");
                return 1;
            }

            Console.WriteLine("Initializing Dropbox client...");
            var dropbox = new DropboxClient(
                accessToken: dropboxAccessToken,
                refreshToken: dropboxRefreshToken,
                appKey: dropboxAppKey,
                appSecret: dropboxAppSecret
            );

            var handler = new Handler(dropbox, localFolder, serverJar, backupKeep);

            // 🛡 Enable Safe Shutdown — wait up to 60 seconds for cleanup
            ShutdownGuard.Enable(timeoutMs: 60000);
            ShutdownGuard.OnShutdown = async () =>
            {
                Console.WriteLine("\n[ShutdownGuard] Detected shutdown signal — performing safe upload...");
                await handler.SafeShutdownAsync();
                Console.WriteLine("[ShutdownGuard] Safe shutdown complete. You may now close the window.");
            };

            await handler.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
    }
}
