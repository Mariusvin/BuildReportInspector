using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.BuildReportInspector
{
    public struct ContentEntry
    {
        public string path;        // Source path in the project
        public ulong size;         // bytes
        public string outputFile;  // name of file in the build output directory
        public string internalArchivePath; // name of file inside AssetBundle (empty for player build)
        public string type;
        public int objectCount;
        public string extension;   // File extension of the source path (e.g. ".jpg")

        public Texture icon; // Only required when displaying in the UI
    }

    public class ContentAnalysis
    {
        public List<ContentEntry> m_assets; // records contents of the build output.  Objects of the same type within the same file are collapsed together to single entry
        public Dictionary<string, ulong> m_outputFiles; // Filepath -> size (Sorted biggest to smallest)
        public Dictionary<string, ulong> m_assetTypes;  // Type -> size (Sorted biggest to smallest)

        private static readonly Texture DefaultAssetIcon = EditorGUIUtility.IconContent("DefaultAsset Icon").image;

        bool m_calculateIcon = true;
        int m_maxEntries = 0;

        public ContentAnalysis(BuildReport report, int maxEntries, bool calculateIcon)
        {
            m_maxEntries = maxEntries;
            m_calculateIcon = calculateIcon;
            CalculateStats(report);
        }

        public bool HitMaximumEntries() { return m_assets.Count == m_maxEntries; }

        private void CalculateStats(BuildReport report)
        {
            m_assets = new List<ContentEntry>();
            m_outputFiles = new Dictionary<string, ulong>();
            m_assetTypes = new Dictionary<string, ulong>();

            // Initialize the FileListHelper
            var fileListHelper = new FileListHelper(report);

            foreach (var packedAsset in report.packedAssets)
            {
                string outputFile;
                string internalArchivePath = "";

                // Look up the archive name for the current internal file name
                var archiveName = fileListHelper.GetArchiveNameForInternalName(packedAsset.shortPath);
                if (!string.IsNullOrEmpty(archiveName))
                {
                    internalArchivePath = packedAsset.shortPath;
                    outputFile = archiveName;
                }
                else
                {
                    outputFile = packedAsset.shortPath;
                }

                if (!m_outputFiles.ContainsKey(outputFile))
                    m_outputFiles[outputFile] = 0;
                m_outputFiles[outputFile] += packedAsset.overhead;

                // Combine all objects that have the same type and source asset
                var assetTypesInFile = new Dictionary<string, ContentEntry>();
                foreach (var entry in packedAsset.contents)
                {
                    var type = entry.type.ToString();

                    if (type.EndsWith("Importer"))
                        type = type.Substring(0, type.Length - 8);

                    var key = type + entry.sourceAssetGUID.ToString();

                    if (assetTypesInFile.ContainsKey(key))
                    {
                        // Update existing entry
                        var existingEntry = assetTypesInFile[key];
                        existingEntry.size += entry.packedSize;
                        existingEntry.objectCount++;
                        assetTypesInFile[key] = existingEntry;
                    }
                    else
                    {
                        string path = entry.sourceAssetPath;
                        if (string.IsNullOrEmpty(path))
                            path = "Generated"; // Build output not associated with a source asset

                        var extension = "";
                        try
                        {
                            extension = Path.GetExtension(path);
                        }
                        catch (Exception)
                        {
                            // Some internal objects have invalid paths
                            // `Built-in Texture2D: sactx-0-1024x512-DXT5|BC3-_mainmenu-936da5f0`
                            // so silently treat it as empty extension
                        }

                        assetTypesInFile[key] = new ContentEntry
                        {
                            size = entry.packedSize,
                            icon = m_calculateIcon ? AssetDatabase.GetCachedIcon(entry.sourceAssetPath) ?? DefaultAssetIcon : null,
                            outputFile = outputFile,
                            internalArchivePath = internalArchivePath,
                            type = type,
                            path = path,
                            extension = extension,
                            objectCount = 1
                        };
                    }
                }

                foreach (var entry in assetTypesInFile)
                {
                    m_assets.Add(entry.Value);

                    var sizeProp = entry.Value.size;
                    m_outputFiles[outputFile] += sizeProp;

                    if (!m_assetTypes.ContainsKey(entry.Value.type))
                        m_assetTypes[entry.Value.type] = 0;

                    m_assetTypes[entry.Value.type] += sizeProp;

                    if (m_assets.Count == m_maxEntries)
                        break;
                }

                if (m_assets.Count == m_maxEntries)
                    break;
            }

            // Sort assets, output files, and asset types in descending order by size
            m_assets = m_assets.OrderBy(p => ulong.MaxValue - p.size).ToList();
            m_outputFiles = m_outputFiles.OrderBy(p => ulong.MaxValue - p.Value).ToDictionary(x => x.Key, x => x.Value);
            m_assetTypes = m_assetTypes.OrderBy(p => ulong.MaxValue - p.Value).ToDictionary(x => x.Key, x => x.Value);
        }
    }
}
