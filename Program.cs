﻿using System;
using System.IO;
using System.Threading.Tasks;
using AOFL.KrakenIoc.Core.V1;
using AOFL.KrakenIoc.Core.V1.Interfaces;

namespace CometPeak.FileCopier {
    public class Program {
        public static async Task Main(string[] args) {
            string currentDirectory = Environment.CurrentDirectory;

            using (IContainer container = new Container()) {
                container.Bind<IFileCopySettingsSystem, FileCopySettingsSystem>();
                container.Bind<IFileCopySystem, FileCopySystem>();

                IFileCopySettingsSystem settingsSystem = container.Resolve<IFileCopySettingsSystem>();
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

                FileCopySettings settings = await settingsSystem.CombineSettings(userConfigPath, projectConfigPath);

                string projectPath = (projectConfigPath == null) ? null : Path.GetDirectoryName(projectConfigPath);
                string listenDirectory = (projectPath != null) ? projectPath : currentDirectory;

                Console.WriteLine("Listening in directory:    " + listenDirectory + "\n");
                IFileCopySystem copySystem = container.Resolve<IFileCopySystem>();
                await copySystem.StartListening(listenDirectory, settings);
            }
        }
    }
}
