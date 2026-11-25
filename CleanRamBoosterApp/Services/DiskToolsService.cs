using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CleanRamBoosterApp.Services
{
    public class DuplicateFileGroup
    {
        public long FileSize { get; set; }
        public string Hash { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
    }

    public class LargeFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class DiskScanProgress
    {
        public int FilesScanned { get; set; }
        public string CurrentFolder { get; set; } = string.Empty;
        public int MatchesFound { get; set; }
    }

    public class DiskToolsService
    {
        // -------------------------------------------------------------
        // 1. DUPLICATE FILE FINDER (Safe Recursive Traversal & MD5 — Unlimited)
        // -------------------------------------------------------------
        public Task<List<DuplicateFileGroup>> FindDuplicatesAsync(
            string rootPath,
            IProgress<DiskScanProgress>? progress = null,
            CancellationToken token = default,
            int maxFiles = int.MaxValue)
        {
            return Task.Run(() => FindDuplicatesCore(rootPath, progress, token, maxFiles), token);
        }

        private List<DuplicateFileGroup> FindDuplicatesCore(
            string rootPath,
            IProgress<DiskScanProgress>? progress,
            CancellationToken token,
            int maxFiles)
        {
            var duplicates = new List<DuplicateFileGroup>();
            var sizeMap = new Dictionary<long, List<string>>();
            int count = 0;

            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(rootPath);

            // Phase 1: Group files by exact size
            while (dirQueue.Count > 0 && !token.IsCancellationRequested && count < maxFiles)
            {
                string currentDir = dirQueue.Dequeue();

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    // Enqueue subdirectories safely
                    foreach (var sub in Directory.GetDirectories(currentDir))
                    {
                        try
                        {
                            var subInfo = new DirectoryInfo(sub);
                            if ((subInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                                dirQueue.Enqueue(sub);
                        }
                        catch { }
                    }
                }
                catch { continue; }

                // Process files in currentDir safely
                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch { continue; }

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested || count >= maxFiles) break;
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length > 2048) // Ignore empty or tiny files
                        {
                            if (!sizeMap.TryGetValue(fi.Length, out var bucket))
                            {
                                bucket = new List<string>();
                                sizeMap[fi.Length] = bucket;
                            }

                            bucket.Add(file);
                            count++;

                            if (count % 120 == 0)
                            {
                                progress?.Report(new DiskScanProgress
                                {
                                    FilesScanned = count,
                                    CurrentFolder = currentDir,
                                    MatchesFound = sizeMap.Count(x => x.Value.Count > 1)
                                });
                            }
                        }
                    }
                    catch { }
                }
            }

            // Phase 2: Compute MD5 hash only for files with identical size
            var sameSizeCandidates = sizeMap.Where(x => x.Value.Count > 1).ToList();
            using var md5 = MD5.Create();
            int processedSizeGroups = 0;

            foreach (var kvp in sameSizeCandidates)
            {
                if (token.IsCancellationRequested) break;

                var hashMap = new Dictionary<string, List<string>>();
                foreach (var file in kvp.Value)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        // Read first 64KB + tail 64KB for large files to eliminate false positives while staying fast
                        int headLen = (int)Math.Min(65536, stream.Length);
                        byte[] headBuf = new byte[headLen];
                        int readHead = stream.Read(headBuf, 0, headLen);
                        md5.Initialize();
                        if (stream.Length > 131072)
                        {
                            md5.TransformBlock(headBuf, 0, readHead, null, 0);
                            stream.Seek(-65536, SeekOrigin.End);
                            byte[] tailBuf = new byte[65536];
                            int readTail = stream.Read(tailBuf, 0, tailBuf.Length);
                            md5.TransformFinalBlock(tailBuf, 0, readTail);
                        }
                        else
                        {
                            md5.TransformFinalBlock(headBuf, 0, readHead);
                        }
                        string hash = Convert.ToHexString(md5.Hash ?? Array.Empty<byte>());

                        if (!hashMap.TryGetValue(hash, out var hBucket))
                        {
                            hBucket = new List<string>();
                            hashMap[hash] = hBucket;
                        }
                        hBucket.Add(file);
                    }
                    catch { }
                }

                foreach (var hGroup in hashMap.Where(x => x.Value.Count > 1))
                {
                    duplicates.Add(new DuplicateFileGroup
                    {
                        FileSize = kvp.Key,
                        Hash = hGroup.Key,
                        FilePaths = hGroup.Value
                    });
                }

                processedSizeGroups++;
                if (processedSizeGroups % 15 == 0)
                {
                    progress?.Report(new DiskScanProgress
                    {
                        FilesScanned = count,
                        CurrentFolder = $"Analyzing duplicate hashes ({processedSizeGroups}/{sameSizeCandidates.Count})...",
                        MatchesFound = duplicates.Count
                    });
                }
            }

            return duplicates;
        }

        public List<DuplicateFileGroup> FindDuplicates(string rootPath, int maxFiles = int.MaxValue)
        {
            return FindDuplicatesCore(rootPath, null, CancellationToken.None, maxFiles);
        }

        // -------------------------------------------------------------
        // 2. LARGE FILE FINDER (Safe Recursive Traversal — Unlimited)
        // -------------------------------------------------------------
        public Task<List<LargeFileInfo>> FindLargeFilesAsync(
            string rootPath,
            long minSizeBytes = 100 * 1024 * 1024,
            int limit = int.MaxValue,
            IProgress<DiskScanProgress>? progress = null,
            CancellationToken token = default)
        {
            return Task.Run(() => FindLargeFilesCore(rootPath, minSizeBytes, limit, progress, token), token);
        }

        private List<LargeFileInfo> FindLargeFilesCore(
            string rootPath,
            long minSizeBytes,
            int limit,
            IProgress<DiskScanProgress>? progress,
            CancellationToken token)
        {
            var results = new List<LargeFileInfo>();
            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(rootPath);
            int scannedCount = 0;

            while (dirQueue.Count > 0 && !token.IsCancellationRequested)
            {
                string currentDir = dirQueue.Dequeue();

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    foreach (var sub in Directory.GetDirectories(currentDir))
                    {
                        try
                        {
                            var subInfo = new DirectoryInfo(sub);
                            if ((subInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                                dirQueue.Enqueue(sub);
                        }
                        catch { }
                    }
                }
                catch { continue; }

                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch { continue; }

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;
                    scannedCount++;
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length >= minSizeBytes)
                        {
                            results.Add(new LargeFileInfo
                            {
                                FilePath = fi.FullName,
                                FileName = fi.Name,
                                SizeBytes = fi.Length,
                                LastModified = fi.LastWriteTime
                            });
                        }
                    }
                    catch { }

                    if (scannedCount % 150 == 0)
                    {
                        progress?.Report(new DiskScanProgress
                        {
                            FilesScanned = scannedCount,
                            CurrentFolder = currentDir,
                            MatchesFound = results.Count
                        });
                    }
                }
            }

            var ordered = results.OrderByDescending(x => x.SizeBytes);
            return limit < int.MaxValue ? ordered.Take(limit).ToList() : ordered.ToList();
        }

        public List<LargeFileInfo> FindLargeFiles(string rootPath, long minSizeBytes = 100 * 1024 * 1024, int limit = int.MaxValue)
        {
            return FindLargeFilesCore(rootPath, minSizeBytes, limit, null, CancellationToken.None);
        }

        // -------------------------------------------------------------
        // 3. EMPTY FOLDER SCANNER
        // -------------------------------------------------------------
        private static readonly string[] ProtectedRootSubdirs =
        {
            "windows",
            "$recycle.bin",
            "system volume information",
            "recovery",
            "perflogs",
            "config.msi",
            "program files\\windowsapps",
            "program files\\microsoft office",
            "program files (x86)\\microsoft",
            "programdata\\microsoft",
            "programdata\\packages"
        };

        private static bool IsProtectedSystemDirectory(string fullPath, string scanRoot)
        {
            try
            {
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                if (string.IsNullOrEmpty(root)) return false;

                string rel = fullPath.Length > root.Length
                    ? fullPath.Substring(root.Length).Trim('\\')
                    : string.Empty;
                if (string.IsNullOrEmpty(rel)) return false;

                // Always protect core Windows OS directories; skip Office/Packages subdirs when scanning from drive root
                bool scanningDriveRoot = string.Equals(
                    Path.GetFullPath(scanRoot).TrimEnd('\\') + "\\",
                    root,
                    StringComparison.OrdinalIgnoreCase);

                foreach (var p in ProtectedRootSubdirs)
                {
                    bool isCoreOs = p == "windows" || p == "$recycle.bin" || p == "system volume information" || p == "recovery" || p == "program files\\windowsapps";
                    if (!isCoreOs && !scanningDriveRoot) continue;
                    if (rel.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                        rel.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public List<string> FindEmptyFolders(string rootPath, int max = int.MaxValue)
        {
            return FindEmptyFoldersCore(rootPath, null, CancellationToken.None, max);
        }

        public Task<List<string>> FindEmptyFoldersAsync(
            string rootPath,
            IProgress<DiskScanProgress>? progress = null,
            CancellationToken token = default,
            int max = int.MaxValue)
        {
            return Task.Run(() => FindEmptyFoldersCore(rootPath, progress, token, max), token);
        }

        private static List<string> FindEmptyFoldersCore(
            string rootPath,
            IProgress<DiskScanProgress>? progress,
            CancellationToken token,
            int max)
        {
            var emptyFolders = new List<string>();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return emptyFolders;

            string normalizedRoot = Path.GetFullPath(rootPath);
            int scannedDirs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportMs = 0;

            ScanEmptyRecursive(normalizedRoot, normalizedRoot, emptyFolders, max, ref scannedDirs, sw, ref lastReportMs, progress, token);

            progress?.Report(new DiskScanProgress
            {
                FilesScanned = scannedDirs,
                CurrentFolder = normalizedRoot,
                MatchesFound = emptyFolders.Count
            });

            return emptyFolders;
        }

        /// <summary>
        /// Post-order DFS returning true if <paramref name="currentDir"/> contains no files and all of its
        /// subdirectories are also empty (so both leaf empty folders and empty parent wrapper directories
        /// are listed accurately with no arbitrary cap).
        /// </summary>
        private static bool ScanEmptyRecursive(
            string currentDir,
            string scanRoot,
            List<string> result,
            int max,
            ref int scannedDirs,
            System.Diagnostics.Stopwatch sw,
            ref long lastReportMs,
            IProgress<DiskScanProgress>? progress,
            CancellationToken token)
        {
            if (token.IsCancellationRequested || result.Count >= max)
                return false;

            scannedDirs++;
            if (progress != null && (scannedDirs % 32 == 0 || sw.ElapsedMilliseconds - lastReportMs >= 100))
            {
                lastReportMs = sw.ElapsedMilliseconds;
                progress.Report(new DiskScanProgress
                {
                    FilesScanned = scannedDirs,
                    CurrentFolder = currentDir,
                    MatchesFound = result.Count
                });
            }

            try
            {
                bool isRoot = currentDir.Equals(scanRoot, StringComparison.OrdinalIgnoreCase);
                if (!isRoot && IsProtectedSystemDirectory(currentDir, scanRoot))
                {
                    return false;
                }

                var dirInfo = new DirectoryInfo(currentDir);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                bool hasAnyFile;
                try
                {
                    hasAnyFile = Directory.EnumerateFiles(currentDir).Any();
                }
                catch
                {
                    return false;
                }

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch
                {
                    return false;
                }

                bool allSubDirsEmpty = true;
                foreach (var sub in subDirs)
                {
                    if (token.IsCancellationRequested || result.Count >= max)
                    {
                        allSubDirsEmpty = false;
                        break;
                    }

                    bool subEmpty = ScanEmptyRecursive(sub, scanRoot, result, max, ref scannedDirs, sw, ref lastReportMs, progress, token);
                    if (!subEmpty)
                    {
                        allSubDirsEmpty = false;
                    }
                }

                if (!isRoot && !hasAnyFile && allSubDirsEmpty && !token.IsCancellationRequested)
                {
                    if (result.Count < max)
                    {
                        result.Add(currentDir);
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteEmptyFolder(string folderPath) => DeleteEmptyFolder(folderPath, out _);

        public bool DeleteEmptyFolder(string folderPath, out string? errorReason)
        {
            errorReason = null;
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    errorReason = "Directory no longer exists.";
                    return false;
                }

                var di = new DirectoryInfo(folderPath);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    errorReason = "Junction / reparse point skipped.";
                    return false;
                }

                if (Directory.EnumerateFileSystemEntries(folderPath).Any())
                {
                    errorReason = "Directory is no longer empty.";
                    return false;
                }

                if ((di.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    di.Attributes = FileAttributes.Directory;
                }

                Directory.Delete(folderPath, false);
                return true;
            }
            catch (Exception ex)
            {
                errorReason = ex.Message;
                return false;
            }
        }
    }
}
