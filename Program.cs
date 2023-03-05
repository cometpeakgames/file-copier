using System;
using System.IO;
using System.Threading.Tasks;
using AOFL.KrakenIoc.Core.V1;
using AOFL.KrakenIoc.Core.V1.Interfaces;

namespace CometPeak.FileSyncSystem {
    public class Program {
        public static async Task Main(string[] args) {
            string currentDirectory = Environment.CurrentDirectory;
            currentDirectory = FileUtility.SanitizePath(currentDirectory);

            Console.WriteLine("File Sync System starting in " + currentDirectory + " ...\n");

            using (IContainer container = new Container()) {
                container.Bind<IFileSyncSettingsSystem, FileSyncSettingsSystem>();
                container.Bind<IFileSyncSystem, FileSyncSystem>();

                IFileSyncSettingsSystem settingsSystem = container.Resolve<IFileSyncSettingsSystem>();
                settingsSystem.FindConfigFilePaths(currentDirectory, out string userConfigPath, out string projectConfigPath);

                bool printed = false;
                if (userConfigPath != null) {
                    printed = true;
                    Console.WriteLine("FOUND user config at:    " + userConfigPath);
                }
                if (userConfigPath != null) {
                    printed = true;
                    Console.WriteLine("FOUND project config at:    " + projectConfigPath);
                }
                if (printed)
                    Console.WriteLine();

                FileSyncSettings settings = await settingsSystem.CombineSettings(userConfigPath, projectConfigPath);

                string projectPath = (projectConfigPath == null) ? null : Path.GetDirectoryName(projectConfigPath);
                string listenDirectory = (projectPath != null) ? projectPath : currentDirectory;

                IFileSyncSystem sync = container.Resolve<IFileSyncSystem>();
                await sync.StartListening(listenDirectory, settings);
            }
        }
    }
}
