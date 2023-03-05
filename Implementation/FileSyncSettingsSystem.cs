using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CometPeak.FileSyncSystem {
    public class FileSyncSettingsSystem : IFileSyncSettingsSystem {
        private const string ProjectConfigFileName = "FileSync-ProjectConfig.json";
        private const string UserConfigFileName = "FileSync-UserConfig.json";

        public FileSyncSettingsSystem() { }

        public void FindConfigFilePaths(string currentDirectory, out string userConfigPath, out string projectConfigPath) {
            userConfigPath = null;
            projectConfigPath = null;
            
            foreach (string folderPath in FileUtility.GetParentFolders(currentDirectory)) {
                string[] filePaths = Directory.GetFiles(folderPath, "*.json");
                foreach (string filePath in filePaths.Select(p => FileUtility.SanitizePath(p))) {
                    string fileName = Path.GetFileName(filePath);
                    switch (fileName) {
                        case UserConfigFileName:
                            userConfigPath = filePath;
                            if (projectConfigPath != null)
                                return;
                            break;
                        case ProjectConfigFileName:
                            projectConfigPath = filePath;
                            if (userConfigPath != null)
                                return;
                            break;
                    }
                }
            }
        }

        public async Task<FileSyncSettings> CombineSettings(string userConfigPath, string projectConfigPath) {
            FileSyncSettings userSettings = null;
            FileSyncSettings projectSettings = null;
            JObject userJson = null;
            JObject projectJson = null;

            async Task<(FileSyncSettings, JObject)> LoadAsync(string configFilePath) {
                using (FileStream file = File.OpenRead(configFilePath))
                using (StreamReader reader = new(file))
                using (JsonTextReader jsonReader = new(reader)) {
                    JObject json = (JObject) await JObject.ReadFromAsync(jsonReader);

                    //NOTE: We CANNOT do this, because we already advanced the jsonReader to the end of the stream...
                    //      serializer.Value.Deserialize<FileCopySettings>(jsonReader);
                    FileSyncSettings settings = json.ToObject<FileSyncSettings>();
                    return (settings, json);
                }
            }

            if (userConfigPath != null) {
                (FileSyncSettings, JObject) pair = await LoadAsync(userConfigPath);
                userSettings = pair.Item1;
                userJson = pair.Item2;
            }
            if (projectConfigPath != null) {
                (FileSyncSettings, JObject) pair = await LoadAsync(projectConfigPath);
                projectSettings = pair.Item1;
                projectJson = pair.Item2;
            }

            FileSyncSettings combined;
            int foundCount = 0;
            if (userSettings != null)
                foundCount++;
            if (projectSettings != null)
                foundCount++;

            if (foundCount >= 2) {
                combined = new();

                FieldInfo[] allFields = typeof(FileSyncSettings).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetField | BindingFlags.SetField);

                foreach (FieldInfo field in allFields) {
                    bool hasUserValue = userJson.ContainsKey(field.Name) && userJson[field.Name].Type != JTokenType.Null;
                    bool hasProjectValue = projectJson.ContainsKey(field.Name) && projectJson[field.Name].Type != JTokenType.Null;

                    if (hasUserValue)
                        field.SetValue(combined, field.GetValue(userSettings));
                    else if (hasProjectValue)
                        field.SetValue(combined, field.GetValue(projectSettings));
                }
            } else if (foundCount == 1) {
                combined = (userSettings == null) ? projectSettings : userSettings;
            } else {
                combined = new();
            }

            Console.WriteLine("Settings: " + JObject.FromObject(combined).ToString(Formatting.Indented) + "\n");
            return combined;
        }
    }
}
