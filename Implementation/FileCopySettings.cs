using System;

namespace CometPeak.FileCopier {
    [Serializable]
    public class FileCopySettings {
        public string[] srcFiles;
        public string[] ignoreFiles;
        public string outputFolder;

        public FileCopySettings() { }
    }
}
