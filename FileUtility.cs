using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CometPeak.FileSyncSystem {
    public static class FileUtility {
        public static string SanitizePath(string path) {
            string result = path.Replace('\\', '/');
            result = Regex.Replace(result, "/{2,}", "/");
            return result;
        }

        public static IEnumerable<string> GetParentFolders(string currentDirectory) {
            currentDirectory = SanitizePath(currentDirectory);
            string[] folderNames = currentDirectory.Split('/');

            int originalLength = currentDirectory.Length;
            int lengthSubtracted = 0;

            for (int i = folderNames.Length - 1; i >= 0; i--) {
                string folderPath = currentDirectory.Substring(0, originalLength - lengthSubtracted);
                yield return folderPath;

                lengthSubtracted += folderNames[i].Length;
                if (i > 1)
                    lengthSubtracted++; //for the /
            }
        }
    }
}
