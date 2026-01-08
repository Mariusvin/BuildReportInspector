using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using Object = UnityEngine.Object;
#if UNITY_ADDRESSABLES
using UnityEditor.AddressableAssets.Build;
#endif

namespace Unity.BuildReportInspector
{
    public class BuildReportInspectorWindow : EditorWindow
    {
        private enum SortColumn { Name, Size }
        private enum SortDirection { Asc, Desc }

        private BuildReport _report;
        private VisualElement _root;
        private Dictionary<string, VisualElement> _tabs;
        private string _activeTab;
        private SortColumn _sortColumn = SortColumn.Name;
        private SortDirection _sortDirection = SortDirection.Asc;

        [MenuItem("Window/Build Report Inspector")]
        public static void ShowWindow()
        {
            GetWindow<BuildReportInspectorWindow>("Build Report Inspector");
        }

        // Allow opening a BuildReport asset directly
        public static void OpenReport(BuildReport report)
        {
            var window = GetWindow<BuildReportInspectorWindow>("Build Report Inspector");
            window._report = report;
            window.RefreshUI();

            // Conditionally show Addressables tab
            _root.Q("addressables-tab").style.display = IsAddressablesPackageInstalled() ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool IsAddressablesPackageInstalled()
        {
            // A reliable way to check for Addressables is to see if its settings object exists.
            return AssetDatabase.FindAssets("t:AddressableAssetSettings").Length > 0;
        }

        private void OnEnable()
        {
            _root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.build-report-inspector/Editor/BuildReportInspectorWindow.uxml");
            if (visualTree == null)
            {
                // Failsafe if UXML is not found
                _root.Add(new Label("Error: Could not load UXML file."));
                return;
            }
            var uxml = visualTree.CloneTree();
            _root.Add(uxml);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.build-report-inspector/Editor/BuildReportInspectorWindow.uss");
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }

            RegisterTabCallbacks();
            RegisterSortHandlers();
            SwitchTab("overview");

            // Add search functionality
            var searchField = _root.Q<ToolbarSearchField>("search-field");
            searchField.RegisterValueChangedCallback(evt => RenderFileListTab(evt.newValue));

            // When a new BuildReport is selected in the Project view, update the window
            Selection.selectionChanged += OnSelectionChanged;

            // Load the last build report if available, or the selected one
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeObject is BuildReport report)
            {
                _report = report;
                RefreshUI();
            }
            else if (_report == null && File.Exists("Library/LastBuild.buildreport"))
            {
                _report = AssetDatabase.LoadAssetAtPath<BuildReport>("Library/LastBuild.buildreport");
                RefreshUI();
            }
        }

        private void RegisterTabCallbacks()
        {
            _tabs = new Dictionary<string, VisualElement>
            {
                ["overview"] = _root.Q("overview-content"),
                ["file-list"] = _root.Q("file-list-content"),
                ["build-steps"] = _root.Q("build-steps-content"),
                ["source-assets"] = _root.Q("source-assets-content"),
                ["addressables"] = _root.Q("addressables-content"),
                ["android-pad"] = _root.Q("android-pad-content"),
                ["stripping"] = _root.Q("stripping-content"),
                ["scenes-using-assets"] = _root.Q("scenes-using-assets-content"),
                ["duplicate-assets"] = _root.Q("duplicate-assets-content")
            };

            _root.Q<Button>("overview-tab").clicked += () => SwitchTab("overview");
            _root.Q<Button>("file-list-tab").clicked += () => SwitchTab("file-list");
            _root.Q<Button>("build-steps-tab").clicked += () => SwitchTab("build-steps");
            _root.Q<Button>("source-assets-tab").clicked += () => SwitchTab("source-assets");
            _root.Q<Button>("addressables-tab").clicked += () => SwitchTab("addressables");
            _root.Q<Button>("android-pad-tab").clicked += () => SwitchTab("android-pad");
            _root.Q<Button>("stripping-tab").clicked += () => SwitchTab("stripping");
            _root.Q<Button>("scenes-using-assets-tab").clicked += () => SwitchTab("scenes-using-assets");
            _root.Q<Button>("duplicate-assets-tab").clicked += () => SwitchTab("duplicate-assets");
        }

        private void SwitchTab(string tabName)
        {
            _activeTab = tabName;
            foreach (var tab in _tabs)
            {
                if (tab.Value != null)
                    tab.Value.style.display = tab.Key == tabName ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_report == null)
            {
                var overviewContainer = _root.Q("overview-content");
                if (overviewContainer != null)
                {
                    overviewContainer.Clear();
                    overviewContainer.Add(new Label("No Build Report loaded. Select a BuildReport asset or go to Window > Open Last Build Report."));
                }
                return;
            }

            _root.Q("android-pad-tab").style.display = _report.summary.platform == BuildTarget.Android ? DisplayStyle.Flex : DisplayStyle.None;
            var buildType = GetBuildType();
            _root.Q("stripping-tab").style.display = buildType == "Player" ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q("scenes-using-assets-tab").style.display = buildType == "Player" ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q("duplicate-assets-tab").style.display = buildType != "Player" ? DisplayStyle.Flex : DisplayStyle.None;

            switch (_activeTab)
            {
                case "overview":
                    RenderOverviewTab();
                    break;
                case "file-list":
                    RenderFileListTab();
                    break;
                case "build-steps":
                    RenderBuildStepsTab();
                    break;
                case "source-assets":
                    RenderSourceAssetsTab();
                    break;
                case "addressables":
                    RenderAddressablesTab();
                    break;
                case "android-pad":
                    RenderAndroidPADTab();
                    break;
                case "stripping":
                    RenderStrippingTab();
                    break;
                case "scenes-using-assets":
                    RenderScenesUsingAssetsTab();
                    break;
                case "duplicate-assets":
                    RenderDuplicateAssetsTab();
                    break;
            }
        }

        private void RenderOverviewTab()
        {
            var container = _root.Q("overview-content");
            container.Clear();

            var summaryContainer = new VisualElement { name = "summary-container", style = { marginBottom = 10 } };
            summaryContainer.Add(new Label($"Build Name: {Application.productName}"));
            summaryContainer.Add(new Label($"Platform: {_report.summary.platform}"));
            summaryContainer.Add(new Label($"Total Time: {FormatTime(_report.summary.totalTime)}"));
            summaryContainer.Add(new Label($"Total Size: {FormatSize(_report.summary.totalSize)}"));
            summaryContainer.Add(new Label($"Build Result: {_report.summary.result}"));
            summaryContainer.Add(new Label($"Build Output Path: {_report.summary.outputPath}"));
            container.Add(summaryContainer);

            var chartContainer = new VisualElement { name = "chart-container", style = { flexGrow = 1 } };
            container.Add(chartContainer);
            RenderChart(chartContainer);
        }

        private void RenderAndroidPADTab()
        {
            var container = _root.Q("android-pad-content");
            container.Clear();
            container.Add(new Label("This feature is not yet implemented."));
        }

        private class PADPackData
        {
            public string PackName;
            public string PackType; // "Base", "Install-Time", "Fast-Follow", "On-Demand"
            public ulong Size;
        }

        private void RenderStrippingTab()
        {
            var container = _root.Q("stripping-content");
            container.Clear();
            if (_report.strippingInfo == null)
            {
                container.Add(new Label("No stripping info available."));
                return;
            }

            var treeView = new TreeView();
            treeView.makeItem = () => new Label();
            treeView.bindItem = (element, i) => (element as Label).text = treeView.GetItemDataForIndex<string>(i);

            var rootItems = _report.strippingInfo.includedModules.Select((m, i) => new TreeViewItemData<string>(i, m)).ToList();
            treeView.SetRootItems(rootItems);

            // This is a simplified representation. A full implementation would require
            // recursively building the tree based on `GetReasonsForIncluding`.
            container.Add(treeView);
        }

        private void RenderScenesUsingAssetsTab()
        {
            var container = _root.Q("scenes-using-assets-content");
            container.Clear();
            if (_report.scenesUsingAssets == null || _report.scenesUsingAssets.Length == 0)
            {
                container.Add(new Label("No scene to asset usage data available."));
                return;
            }

            var listView = new ListView();
            listView.makeItem = () =>
            {
                var foldout = new Foldout();
                foldout.contentContainer.Add(new ListView());
                return foldout;
            };

            listView.bindItem = (element, i) =>
            {
                var foldout = element as Foldout;
                var sceneAssetInfo = _report.scenesUsingAssets[0].list[i];
                foldout.text = $"{sceneAssetInfo.assetPath} ({sceneAssetInfo.scenePaths.Length} scenes)";

                var sceneList = foldout.contentContainer.Q<ListView>();
                sceneList.itemsSource = sceneAssetInfo.scenePaths.ToList();
                sceneList.makeItem = () => new Label();
                sceneList.bindItem = (e, j) => (e as Label).text = sceneAssetInfo.scenePaths[j];
            };

            listView.itemsSource = _report.scenesUsingAssets[0].list.ToList();
            container.Add(listView);
        }

        private DuplicateAssets _duplicateAssetsAnalysis;
        private void RenderDuplicateAssetsTab()
        {
            var container = _root.Q("duplicate-assets-content");
            container.Clear();

            if (_duplicateAssetsAnalysis == null)
            {
                var calculateButton = new Button(() =>
                {
                    _duplicateAssetsAnalysis = new DuplicateAssets(_report);
                    RenderDuplicateAssetsTab();
                })
                {
                    text = "Calculate"
                };
                container.Add(calculateButton);
                return;
            }

            if (_duplicateAssetsAnalysis.m_AssetStats.Count == 0)
            {
                container.Add(new Label("No duplicated assets discovered."));
                return;
            }

            var listView = new ListView();
            listView.makeItem = () =>
            {
                var foldout = new Foldout();
                foldout.contentContainer.Add(new ListView());
                return foldout;
            };

            listView.bindItem = (element, i) =>
            {
                var foldout = element as Foldout;
                var assetStat = _duplicateAssetsAnalysis.m_AssetStats.ElementAt(i);
                foldout.text = $"{assetStat.Key} ({FormatSize(assetStat.Value.totalSize)})";

                var bundleList = foldout.contentContainer.Q<ListView>();
                bundleList.itemsSource = assetStat.Value.assetBundleSizes.ToList();
                bundleList.makeItem = () => new Label();
                bundleList.bindItem = (e, j) =>
                {
                    var bundleStat = assetStat.Value.assetBundleSizes.ElementAt(j);
                    (e as Label).text = $"{bundleStat.Key} ({FormatSize(bundleStat.Value)})";
                };
            };

            listView.itemsSource = _duplicateAssetsAnalysis.m_AssetStats.ToList();
            container.Add(listView);
        }

        private void RenderBuildStepsTab()
        {
            var container = _root.Q("build-steps-content");
            container.Clear();
            var treeView = new TreeView();
            treeView.makeItem = () => new Label();
            treeView.bindItem = (element, i) => (element as Label).text = treeView.GetItemDataForIndex<BuildStep>(i).name;

            var rootItems = _report.steps.Select((s, i) => new TreeViewItemData<BuildStep>(i, s)).ToList();
            treeView.SetRootItems(rootItems);
            container.Add(treeView);
        }

        private ContentAnalysis _sourceAssetsAnalysis;
        private void RenderSourceAssetsTab()
        {
            var container = _root.Q("source-assets-content");
            container.Clear();

            if (_sourceAssetsAnalysis == null)
            {
                var calculateButton = new Button(() =>
                {
                    _sourceAssetsAnalysis = new ContentAnalysis(_report, 10000, true);
                    RenderSourceAssetsTab();
                })
                {
                    text = "Calculate"
                };
                container.Add(calculateButton);
                return;
            }

            if (_sourceAssetsAnalysis.m_assets.Count == 0)
            {
                container.Add(new Label("No source assets found."));
                return;
            }

            var listView = new ListView();
            listView.makeItem = () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                row.Add(new Label { name = "asset-path", style = { flexGrow = 1 } });
                row.Add(new Label { name = "asset-size", style = { width = 100, unityTextAlign = TextAnchor.MiddleRight } });
                return row;
            };

            listView.bindItem = (element, i) =>
            {
                var data = _sourceAssetsAnalysis.m_assets[i];
                element.Q<Label>("asset-path").text = data.path;
                element.Q<Label>("asset-size").text = FormatSize(data.size);
            };

            listView.itemsSource = _sourceAssetsAnalysis.m_assets;
            container.Add(listView);
        }

        private List<PADPackData> GroupAssetsByPADPack()
        {
            var packData = new List<PADPackData>();
            var baseFiles = new List<BuildFile>();

            // This is a simulation. A real implementation would parse the AssetPackConfig.
            var installTimeFiles = _report.files.Where(f => f.path.Contains("Assets/PlayAssetDelivery/InstallTime")).ToList();
            var fastFollowFiles = _report.files.Where(f => f.path.Contains("Assets/PlayAssetDelivery/FastFollow")).ToList();
            var onDemandFiles = _report.files.Where(f => f.path.Contains("Assets/PlayAssetDelivery/OnDemand")).ToList();

            var allPadFiles = installTimeFiles.Concat(fastFollowFiles).Concat(onDemandFiles).ToList();
            baseFiles = _report.files.Except(allPadFiles).ToList();

            packData.Add(new PADPackData { PackName = "Base", PackType = "Base", Size = (ulong)baseFiles.Sum(f => (long)f.size) });
            packData.Add(new PADPackData { PackName = "InstallTimeExample", PackType = "Install-Time", Size = (ulong)installTimeFiles.Sum(f => (long)f.size) });
            packData.Add(new PADPackData { PackName = "FastFollowExample", PackType = "Fast-Follow", Size = (ulong)fastFollowFiles.Sum(f => (long)f.size) });
            packData.Add(new PADPackData { PackName = "OnDemandExample", PackType = "On-Demand", Size = (ulong)onDemandFiles.Sum(f => (long)f.size) });

            return packData;
        }

        private void RenderAddressablesTab()
        {
            var container = _root.Q("addressables-content");
            container.Clear();

            if (!IsAddressablesPackageInstalled())
            {
                container.Add(new Label("Addressables package not detected."));
                return;
            }

            var addressablesData = ParseAddressablesBuildLogs();
            if (addressablesData.Count == 0)
            {
                container.Add(new Label("No Addressables build data found. Please build Addressables first."));
                return;
            }

            var listView = new ListView();
            listView.makeItem = () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                row.Add(new Label { name = "group-name", style = { flexGrow = 1 } });
                row.Add(new Label { name = "group-size", style = { width = 100, unityTextAlign = TextAnchor.MiddleRight } });
                return row;
            };

            listView.bindItem = (element, i) =>
            {
                var data = addressablesData[i];
                element.Q<Label>("group-name").text = data.GroupName;
                element.Q<Label>("group-size").text = FormatSize(data.Size);
            };

            listView.itemsSource = addressablesData;
            container.Add(listView);
        }

        private class AddressableGroupData
        {
            public string GroupName;
            public ulong Size;
        }

        private List<AddressableGroupData> ParseAddressablesBuildLogs()
        {
#if UNITY_ADDRESSABLES
            var contentStatePath = "Assets/AddressableAssetsData/addressables_content_state.bin";
            if (!File.Exists(contentStatePath))
            {
                return new List<AddressableGroupData>();
            }

            var contentState = ContentUpdateScript.LoadContentState(contentStatePath);
            var buildReportFiles = _report.files.ToDictionary(f => Path.GetFileName(f.path), f => f);

            var addressablesData = new Dictionary<string, ulong>();
            foreach (var cacheInfo in contentState.cachedInfos)
            {
                if (buildReportFiles.TryGetValue(cacheInfo.asset.bundleFileId, out var fileInfo))
                {
                    if (addressablesData.ContainsKey(cacheInfo.asset.addressableGroup))
                    {
                        addressablesData[cacheInfo.asset.addressableGroup] += fileInfo.size;
                    }
                    else
                    {
                        addressablesData[cacheInfo.asset.addressableGroup] = fileInfo.size;
                    }
                }
            }

            return addressablesData.Select(kvp => new AddressableGroupData { GroupName = kvp.Key, Size = kvp.Value }).ToList();
#else
            return new List<AddressableGroupData>();
#endif
        }

        private void RenderChart(VisualElement chartContainer)
        {
            if (_report.files == null || _report.files.Length == 0) return;

            var assetsByType = _report.files
                .Where(f => !string.IsNullOrEmpty(f.path))
                .GroupBy(f => Path.GetExtension(f.path).ToLower())
                .Select(g => new { Type = g.Key, Size = g.Sum(f => (long)f.size) })
                .OrderByDescending(x => x.Size)
                .ToList();

            var totalSize = (long)_report.summary.totalSize;

            foreach (var asset in assetsByType.Take(10)) // Limit to top 10 for readability
            {
                var percentage = (float)asset.Size / totalSize;
                var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 5 } };

                var label = new Label($"{asset.Type} ({FormatSize((ulong)asset.Size)})") { style = { width = 200 } };
                bar.Add(label);

                var progressBar = new VisualElement { style = { flexGrow = 1, height = 20, backgroundColor = new Color(0.3f, 0.3f, 0.3f) } };
                var progressFill = new VisualElement { style = { width = new Length(percentage * 100, LengthUnit.Percent), height = 20, backgroundColor = GetColorForExtension(asset.Type) } };
                progressBar.Add(progressFill);
                bar.Add(progressBar);

                chartContainer.Add(bar);
            }
        }

        private Color GetColorForExtension(string extension)
        {
            switch (extension)
            {
                case ".unity": return new Color(0.2f, 0.5f, 0.8f);
                case ".assets": return new Color(0.8f, 0.2f, 0.2f);
                case ".shader": return new Color(0.8f, 0.2f, 0.8f);
                case ".dll": return new Color(0.2f, 0.8f, 0.2f);
                case ".png":
                case ".jpg":
                case ".tga": return new Color(0.8f, 0.5f, 0.2f);
                default: return Color.gray;
            }
        }

        private void RegisterSortHandlers()
        {
            var nameHeader = _root.Q<Button>("file-name-header");
            var sizeHeader = _root.Q<Button>("file-size-header");

            nameHeader.clicked += () =>
            {
                if (_sortColumn == SortColumn.Name)
                {
                    _sortDirection = _sortDirection == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc;
                }
                else
                {
                    _sortColumn = SortColumn.Name;
                    _sortDirection = SortDirection.Asc;
                }
                RenderFileListTab();
            };

            sizeHeader.clicked += () =>
            {
                if (_sortColumn == SortColumn.Size)
                {
                    _sortDirection = _sortDirection == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc;
                }
                else
                {
                    _sortColumn = SortColumn.Size;
                    _sortDirection = SortDirection.Asc;
                }
                RenderFileListTab();
            };
        }

        private void RenderFileListTab(string filter = "")
        {
            var fileList = _root.Q<ListView>("file-list");
            var files = string.IsNullOrEmpty(filter)
                ? _report.files.ToList()
                : _report.files.Where(f => f.path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Sorting logic
            files.Sort((a, b) =>
            {
                int result;
                if (_sortColumn == SortColumn.Name)
                {
                    result = string.Compare(a.path, b.path, StringComparison.Ordinal);
                }
                else
                {
                    result = a.size.CompareTo(b.size);
                }

                if (_sortDirection == SortDirection.Desc)
                {
                    result *= -1;
                }

                return result;
            });

            fileList.makeItem = () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                row.Add(new Label { name = "file-name", style = { flexGrow = 1 } });
                row.Add(new Label { name = "file-size", style = { width = 100, unityTextAlign = TextAnchor.MiddleRight } });
                return row;
            };

            fileList.bindItem = (element, i) =>
            {
                element.Q<Label>("file-name").text = files[i].path;
                element.Q<Label>("file-size").text = FormatSize(files[i].size);
            };

            fileList.itemsSource = files;
            fileList.Rebuild();
        }

        private static string FormatTime(TimeSpan t) => $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}";
        private static string FormatSize(ulong size)
        {
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{(size / 1024.0):F2} KB";
            if (size < 1024 * 1024 * 1024) return $"{(size / (1024.0 * 1024.0)):F2} MB";
            return $"{(size / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
    }

    [CustomEditor(typeof(BuildReport))]
    public class BuildReportAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open in Build Report Inspector"))
            {
                BuildReportInspectorWindow.OpenReport(target as BuildReport);
            }
        }
    }
}
