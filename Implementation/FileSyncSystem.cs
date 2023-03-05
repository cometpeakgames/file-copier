using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CometPeak.FileSyncSystem {
    public class FileSyncSystem : IFileSyncSystem {
        //NOTE: ConcurrentBags are better used for other scenarios..
        //  - It's an unordered collection of objects that SUPPORTS duplicates
        //  - You can't easily/quickly check if it contains a specific element
        //  - It's optimized for scenarios where the same thread will be both producing and consuming data stored in the bag
        //      (you take ONE item out of the bag at a time!)

        //HOWEVER, we want a unique collection of elements (file paths), and to quickly check if a file path is in-use already.
        //The best collection for this is a ConcurrentDictionary where we ignore the values.

        //To avoid locking files before even the text editor is done with it (like VS Code).
        //We want to always be SECOND to reach the file, if possible..
        private const int FileWatchDelay = 1000;
        private const int MaxRetryTime = 3000;
        private const int RetryDelay = 500;

        private string directory;
        private FileSyncSettings settings;

        private Task running;
        private CancellationToken cancellation;     //WARNING: Accessed on multiple threads!
        private FileSystemWatcher fileWatcher;

        private object syncRoot = new();

        private ConcurrentDictionary<string, DateTime> inProgressIOPaths = new();

        public FileSyncSystem() { }

        public Task StartListening(string directory, FileSyncSettings settings, CancellationToken cancellation) {
            this.directory = directory;
            this.settings = settings;
            SetCancellationToken(cancellation);
            if (running == null || running.IsCompleted)
                running = Run();
            return running;
        }

        public async Task StopListening() {
            SetCancellationToken(new CancellationToken(true));
            await running;
        }

        private async Task Run() {
            await OnStart();

            _ = Task.Run(() => {
                while (CanContinue()) {
                    string input = Console.ReadLine();
                    switch (input) {
                        case "exit":
                            SetCancellationToken(new(true));
                            break;
                        case "clear":
                            lock (syncRoot) {
                                ClearAllFiles().Wait();
                            }
                            break;
                        case "sync":
                            lock (syncRoot) {
                                SyncAllFiles().Wait();
                            }
                            break;
                    }
                }
            });

            while (CanContinue()) {
                await Task.Delay(50);
            }
            OnStop();
        }

        private bool CanContinue() {
            bool result;
            lock (syncRoot) {
                result = !cancellation.IsCancellationRequested;
            }
            return result;
        }

        private void SetCancellationToken(CancellationToken value) {
            lock (syncRoot) {
                cancellation = value;
            }
        }

        private async Task OnStart() {
            fileWatcher = new FileSystemWatcher(directory, "*");
            fileWatcher.EnableRaisingEvents = true;
            fileWatcher.IncludeSubdirectories = true;
            fileWatcher.Created += OnFileCreated;
            fileWatcher.Changed += OnFileChanged;
            fileWatcher.Deleted += OnFileDeleted;
            fileWatcher.Renamed += OnFileRenamed;

            await SyncAllFiles();
        }

        private async Task SyncAllFiles() {
            await Task.Delay(FileWatchDelay);
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (string file in files) {
                string fileName = Path.GetFileName(file);
                if (ShouldBeCopied(settings, fileName))
                    await SyncFile(file, true, 0, "Copying existing file");
            }
        }

        private async Task ClearAllFiles() {
            await Task.Delay(FileWatchDelay);
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (string file in files) {
                string fileName = Path.GetFileName(file);
                if (ShouldBeCopied(settings, fileName))
                    await SyncFile(file, false, 0, "Deleting synced file");
            }
        }

        private void OnStop() {
            fileWatcher.Dispose();
            fileWatcher = null;
        }

        private bool ShouldBeCopied(FileSyncSettings settings, string fileName) {
            if (settings.ignoreFiles != null)
                foreach (string pattern in settings.ignoreFiles)
                    if (Regex.IsMatch(fileName, pattern))
                        return false;
            if (settings.srcFiles != null)
                foreach (string pattern in settings.srcFiles)
                    if (Regex.IsMatch(fileName, pattern))
                        return true;
            return false;
        }

        private async Task<string> TryReadAsync(string filePath) {
            int elapsedTime = 0;
            string fileContents;
            do {
                try {
                    using (FileStream file = File.OpenRead(filePath))
                    using (StreamReader reader = new(file)) {
                        fileContents = await reader.ReadToEndAsync();
                        if (elapsedTime > 0)
                            Console.WriteLine("Done reading!");
                        return fileContents;
                    }
                } catch (IOException e) {
                    fileContents = null;
                    Console.WriteLine(e.GetType().FullName + ": " + e.Message + "\n(Retrying read in " + RetryDelay + "ms)...");

                    await Task.Delay(RetryDelay);
                    elapsedTime += RetryDelay;
                }
            } while (elapsedTime < MaxRetryTime);

            return fileContents;
        }

        private async Task TryWriteAsync(string filePath, string fileContents) {
            int elapsedTime = 0;
            do {
                try {
                    using (StreamWriter writer = new(filePath, false)) {
                        await writer.WriteAsync(fileContents);
                        if (elapsedTime > 0)
                            Console.WriteLine("Done writing!");
                        return;
                    }
                } catch (IOException e) {
                    Console.WriteLine(e.GetType().FullName + ": " + e.Message + "\n(Retrying write in " + RetryDelay + "ms)...");

                    await Task.Delay(RetryDelay);
                    elapsedTime += RetryDelay;
                }
            } while (elapsedTime < MaxRetryTime);
        }

        //TODO: Make this a queue instead? (LAST action takes priority? Command pattern?)
        //Probably doesn't handle file updating vs. deletion concurrently that well..!
        private async Task SyncFile(string filePath, bool fileShouldExist, int delay, string fileActionName = null) {
            try {
                filePath = FileUtility.SanitizePath(filePath);
                string relativePath = FileUtility.SanitizePath(Path.GetRelativePath(directory, filePath));
                string outputFilePath = FileUtility.SanitizePath(Path.Combine(settings.outputFolder, relativePath));

                if (fileShouldExist) {
                    DateTime startTime = DateTime.Now;

                    //NOTE: This fixes issues with VS Code saving every file TWICE within a very short duration (less than 0.01sec)
                    if (inProgressIOPaths.TryGetValue(filePath, out DateTime prevTime))
                        return;
                    inProgressIOPaths.TryAdd(filePath, startTime);
                }

                if (delay > 0)
                    await Task.Delay(delay);

                if (fileActionName != null)
                    Console.WriteLine(fileActionName + " (" + relativePath + ")!");

                string outputFolder = Path.GetDirectoryName(outputFilePath);
                if (fileShouldExist) {
                    //NOTE: We're using FileStreams with explicit File.OpenRead(...) so we don't get into trouble with VS Code (while editing/saving files),
                    //      Because we DO NOT WANT TO LOCK the file at the filePath (src)
                    string fileContents = await TryReadAsync(filePath);

                    Directory.CreateDirectory(outputFolder);

                    await TryWriteAsync(outputFilePath, fileContents);
                } else {
                    File.Delete(outputFilePath);

                    string lastEmpty = null;
                    foreach (string folder in FileUtility.GetParentFolders(outputFolder)) {
                        Console.WriteLine(folder);
                        string[] subfolders = Directory.GetDirectories(folder);
                        string[] files = Directory.GetFiles(folder);

                        if (subfolders.Length == 0 && files.Length == 0)
                            lastEmpty = folder;
                        else
                            break;
                    }
                    if (lastEmpty != null)
                        Directory.Delete(lastEmpty);
                }
            } catch (Exception e) {
                Console.WriteLine("\n" + e.GetType().FullName + ": " + e.Message + "\n" + e.StackTrace);
            } finally {
                if (fileShouldExist)
                    inProgressIOPaths.TryRemove(filePath, out _);
            }

        }

        private void OnFileCreated(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    _ = SyncFile(e.FullPath, true, FileWatchDelay, "Copying new file");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    _ = SyncFile(e.FullPath, true, FileWatchDelay, "Updating existing file");
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    _ = SyncFile(e.FullPath, false, FileWatchDelay, "Deleting file");
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e) {
            lock (syncRoot) {
                string prevFileName = Path.GetFileName(e.OldFullPath);
                string fileName = Path.GetFileName(e.FullPath);

                if (ShouldBeCopied(settings, prevFileName))
                    _ = SyncFile(e.OldFullPath, false, FileWatchDelay, "Deleting file");
                if (ShouldBeCopied(settings, fileName))
                    _ = SyncFile(e.FullPath, true, FileWatchDelay, "Copying new file");
            }
        }
    }
}
