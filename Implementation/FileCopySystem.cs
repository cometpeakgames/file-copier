using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CometPeak.FileCopier {
    public class FileCopySystem : IFileCopySystem {
        private string directory;
        private FileCopySettings settings;

        private Task running;
        private CancellationToken cancellation;     //WARNING: Accessed on multiple threads!
        private FileSystemWatcher fileWatcher;

        private object syncRoot = new();

        public FileCopySystem() { }

        public Task StartListening(string directory, FileCopySettings settings, CancellationToken cancellation) {
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
            OnStart();

            _ = Task.Run(() => {
                while (CanContinue()) {
                    string input = Console.ReadLine();
                    switch (input) {
                        case "exit":
                            SetCancellationToken(new(true));
                            break;
                        case "clear":
                            lock (syncRoot) {
                                ClearAllFiles();
                            }
                            break;
                        case "sync":
                            lock (syncRoot) {
                                SyncAllFiles();
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

        private void OnStart() {
            fileWatcher = new FileSystemWatcher(directory, "*");
            fileWatcher.EnableRaisingEvents = true;
            fileWatcher.IncludeSubdirectories = true;
            fileWatcher.Created += OnFileCreated;
            fileWatcher.Changed += OnFileChanged;
            fileWatcher.Deleted += OnFileDeleted;
            fileWatcher.Renamed += OnFileRenamed;

            SyncAllFiles();
        }

        private void SyncAllFiles() {
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (string file in files) {
                string fileName = Path.GetFileName(file);
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(file, true, "Copying existing file");
            }
        }

        private void ClearAllFiles() {
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (string file in files) {
                string fileName = Path.GetFileName(file);
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(file, false, "Deleting synced file");
            }
        }

        private void OnStop() {
            fileWatcher.Dispose();
            fileWatcher = null;
        }

        private bool ShouldBeCopied(FileCopySettings settings, string fileName) {
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

        private void SyncFile(string filePath, bool fileShouldExist, string fileActionName = null) {
            string relativePath = Path.GetRelativePath(directory, filePath).Replace('\\', '/');
            string outputPath = Path.Combine(settings.outputFolder, relativePath);

            if (fileActionName != null)
                Console.WriteLine(fileActionName + " (" + relativePath + ")!");

            string outputFolder = Path.GetDirectoryName(outputPath);
            if (fileShouldExist) {
                Directory.CreateDirectory(outputFolder);
                File.Copy(filePath, outputPath, true);
            } else {
                File.Delete(outputPath);

                string lastEmpty = null;
                foreach (string folder in FileUtility.GetParentFolders(outputFolder)) {
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
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(e.FullPath, true, "Copying new file");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(e.FullPath, true, "Updating existing file");
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e) {
            lock (syncRoot) {
                string fileName = Path.GetFileName(e.FullPath);
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(e.FullPath, false, "Deleting file");
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e) {
            lock (syncRoot) {
                string prevFileName = Path.GetFileName(e.OldFullPath);
                string fileName = Path.GetFileName(e.FullPath);

                if (ShouldBeCopied(settings, prevFileName))
                    SyncFile(e.OldFullPath, false, "Deleting file");
                if (ShouldBeCopied(settings, fileName))
                    SyncFile(e.FullPath, true, "Copying new file");
            }
        }
    }
}
