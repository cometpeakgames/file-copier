using System.Threading.Tasks;

namespace CometPeak.FileSyncSystem {
    public interface IFileSyncSettingsSystem {
        //NOTE: Precedence of the fields is:
        //      1. the value from the user config
        //      2. the value from the project config
        //      3. default value
        public void FindConfigFilePaths(string currentDirectory, out string userConfigPath, out string projectConfigPath);

        public Task<FileSyncSettings> CombineSettings(string userConfigPath, string projectConfigPath);
    }
}
