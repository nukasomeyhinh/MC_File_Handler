using System.IO.Compression;

public static class Utils
{
    public static bool ZipFolder(string sourceFolder, string destinationZip)
    {
        try
        {
            if (!Directory.Exists(sourceFolder))
            {
                Directory.CreateDirectory(sourceFolder);
            }

            if (File.Exists(destinationZip))
                File.Delete(destinationZip);

            ZipFile.CreateFromDirectory(sourceFolder, destinationZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            Console.WriteLine($"Zipped {sourceFolder} → {destinationZip}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Zip error: {ex.Message}");
            return false;
        }
    }

    public static bool ExtractZip(string zipFile, string destinationFolder)
    {
        try
        {
            if (!File.Exists(zipFile))
            {
                Console.WriteLine($"Extract skipped: not found {zipFile}");
                return false;
            }

            Directory.CreateDirectory(destinationFolder);
            ZipFile.ExtractToDirectory(zipFile, destinationFolder, overwriteFiles: true);
            Console.WriteLine($"Extracted {zipFile} → {destinationFolder}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Extract error: {ex.Message}");
            return false;
        }
    }

    public static void CleanOldBackupsLocal(string backupsFolder, int keep)
    {
        try
        {
            Directory.CreateDirectory(backupsFolder);
            var files = Directory.EnumerateFiles(backupsFolder, "*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
            //There is better compression method but meh I'll change it once the scaling is getting bad

            foreach (var old in files.Skip(keep))
            {
                try
                {
                    File.Delete(old.FullName);
                    Console.WriteLine($"Deleted old backup: {old.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete {old.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Clean backups error: {ex.Message}");
        }
    }
}
