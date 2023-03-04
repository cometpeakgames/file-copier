using System;

namespace CometPeak.FileSyncSystem {
    [Serializable]
    public class FileSyncSettings {
        public string[] srcFiles;
        public string[] ignoreFiles;
        public string outputFolder;

        public FileSyncSettings() { }
    }
}
