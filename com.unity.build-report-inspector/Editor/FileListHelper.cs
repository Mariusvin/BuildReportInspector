using System.Collections.Generic;
using System.IO;
using UnityEditor.Build.Reporting;

namespace Unity.BuildReportInspector
{
    /// <summary>
    /// Helper class for processing file-related operations from the BuildReport.
    /// </summary>
    public class FileListHelper
    {
        private Dictionary<string, string> internalNameToArchiveMapping = new Dictionary<string, string>();

        public FileListHelper(BuildReport report)
        {
            CalculateAssetBundleMapping(report);
        }

        /// <summary>
        /// Given a name of a content file in the build output this will return the Archive name, if it is inside an AssetBundle.
        /// The archive name is the name of the AssetBundle file so this is a more user friendly name than the internal name.
        /// For example GetArchiveNameForInternalName("CAB-76a378bdc9304bd3c3a82de8dd97981a.resource") might return "audio.bundle"
        /// For files in an Player build this always returns null.
        /// </summary>
        /// <param name="internalName">The internal name to map.</param>
        /// <returns>The corresponding archive name, or null if not found.</returns>
        public string GetArchiveNameForInternalName(string internalName)
        {
            return internalNameToArchiveMapping.TryGetValue(internalName, out var archiveName) ? archiveName : null;
        }

        private void CalculateAssetBundleMapping(BuildReport report)
        {
            internalNameToArchiveMapping.Clear();

            var files = report.files;

            var archivePathToFileName = new Dictionary<string, string>();
            foreach (var file in files)
            {
                if (file.role == "AssetBundle")
                {
                    var justFileName = Path.GetFileName(file.path);
                    archivePathToFileName[file.path] = justFileName;
                }
            }

            if (archivePathToFileName.Count == 0)
                return;

            foreach (var file in files)
            {
                var justPath = Path.GetDirectoryName(file.path)?.Replace('\\', '/');
                var justFileName = Path.GetFileName(file.path);

                if (!string.IsNullOrEmpty(justPath) && archivePathToFileName.ContainsKey(justPath))
                {
                    internalNameToArchiveMapping[justFileName] = archivePathToFileName[justPath];
                }
            }
        }
    }
}
