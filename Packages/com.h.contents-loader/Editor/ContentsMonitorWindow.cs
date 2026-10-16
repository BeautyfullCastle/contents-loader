using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace H.ContentsLoader.Editor
{
    // 플레이 중 로드된 콘텐츠와 팩 상태를 보여주고, 설정과 빌드도 여기서 함
    internal sealed class ContentsMonitorWindow : EditorWindow
    {
        private enum Tab { Assets, Packs, Settings }
        private enum StateFilter { All, Loading, Loaded, Cached }
        private enum AssetColumn { State, Path, Type, Owner, Size, LastUsed }
        private enum PackColumn { Pack, Delivery, Location, Size, Memory, Refs, Assets }

        private const float RowHeight = 18f;
        private const float RefreshInterval = 0.25f;

        private static readonly Color LoadingColor = new(0.95f, 0.75f, 0.2f);
        private static readonly Color LoadedColor = new(0.35f, 0.8f, 0.4f);
        private static readonly Color CachedColor = new(0.45f, 0.65f, 0.95f);
        private static readonly Color IdleColor = new(0.5f, 0.5f, 0.5f);

        [SerializeField] private Tab tab;
        [SerializeField] private StateFilter stateFilter;
        [SerializeField] private string search = "";
        [SerializeField] private AssetColumn assetSort = AssetColumn.Path;
        [SerializeField] private PackColumn packSort = PackColumn.Pack;
        [SerializeField] private bool sortDescending;
        [SerializeField] private bool openPacksOnly;
        [SerializeField] private List<string> expandedPacks = new();

        private Vector2 scroll;
        private double nextRefresh;
        private readonly List<AssetRow> assets = new();
        private readonly List<PackInfo> packs = new();
        private int loadingCount, loadedCount, cachedCount;
        private long loadedSize, budget;
        private string backendName = "";
        private Dictionary<string, List<string>> assetPathsOfContent;
        private GUIStyle rightLabel, dot;
        private UnityEditor.Editor settingsEditor;

        private struct AssetRow
        {
            public string Path;
            public Type Type;
            public ContentState State;
            public string Owner;
            public long Size;
            public int FramesAgo;
        }

        [MenuItem("Tools/Contents Loader/Monitor")]
        public static void Open() => Open(Tab.Assets);

        [MenuItem("Tools/Contents Loader/Settings")]
        public static void OpenSettings() => Open(Tab.Settings);

        private static void Open(Tab tab)
        {
            var window = GetWindow<ContentsMonitorWindow>();
            window.titleContent = new GUIContent("Contents Loader");
            window.minSize = new Vector2(520, 240);
            window.tab = tab;
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (settingsEditor != null) DestroyImmediate(settingsEditor);
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            assetPathsOfContent = null;
            Refresh();
            Repaint();
        }

        private void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            assets.Clear();
            packs.Clear();
            loadingCount = loadedCount = cachedCount = 0;
            loadedSize = budget = 0;
            backendName = "";
            if (!EditorApplication.isPlaying || !ContentSystem.IsInitialized) return;

            var lifetime = ContentSystem.Lifetime;
            loadedSize = lifetime.LoadedSize;
            budget = lifetime.MemoryBudget;
            var frame = Time.frameCount;
            foreach (var record in lifetime.Records)
            {
                switch (record.State)
                {
                    case ContentState.Loading: loadingCount++; break;
                    case ContentState.Loaded: loadedCount++; break;
                    case ContentState.Cached: cachedCount++; break;
                }
                assets.Add(new AssetRow
                {
                    Path = record.Path,
                    Type = record.Type,
                    State = record.State,
                    Owner = OwnerText(record),
                    Size = record.Size,
                    FramesAgo = frame - record.LastUsedFrame,
                });
            }

            if (lifetime.Backend is AssetBundleBackend bundles)
            {
                backendName = "AssetBundles";
                packs.AddRange(bundles.GetPackInfos());
            }
            else
            {
                backendName = lifetime.Backend is AssetDatabaseBackend ? "AssetDatabase" : lifetime.Backend.GetType().Name;
            }
        }

        private static string OwnerText(ContentRecord record)
        {
            var names = record.Scenes.Select(s => s.IsValid() ? s.name : "(unloaded)").OrderBy(n => n).ToList();
            if (record.App) names.Insert(0, "App");
            return names.Count == 0 ? "-" : string.Join(", ", names);
        }

        private void OnGUI()
        {
            rightLabel ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
            dot ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };

            DrawToolbar();
            if (tab == Tab.Settings)
            {
                DrawSettings();
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("플레이 모드에서 로드된 콘텐츠와 팩 상태가 여기에 보입니다.", MessageType.Info);
                return;
            }
            if (!ContentSystem.IsInitialized)
            {
                EditorGUILayout.HelpBox("아직 Content를 한 번도 부르지 않아서 Contents Loader가 시작되지 않았습니다.", MessageType.Info);
                return;
            }

            DrawSummary();
            if (tab == Tab.Assets) DrawAssets();
            else DrawPacks();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var newTab = (Tab)GUILayout.Toolbar((int)tab, new[] { "Assets", "Packs", "Settings" }, EditorStyles.toolbarButton, GUILayout.Width(210));
                if (newTab != tab)
                {
                    tab = newTab;
                    scroll = Vector2.zero;
                    sortDescending = false;
                }

                if (tab == Tab.Settings)
                {
                    GUILayout.FlexibleSpace();
                    return;
                }

                if (tab == Tab.Assets)
                    stateFilter = (StateFilter)EditorGUILayout.EnumPopup(stateFilter, EditorStyles.toolbarPopup, GUILayout.Width(80));
                else
                    openPacksOnly = GUILayout.Toggle(openPacksOnly, "Open only", EditorStyles.toolbarButton, GUILayout.Width(80));

                GUILayout.Space(6);
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(80));
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || cachedCount == 0))
                {
                    if (GUILayout.Button(new GUIContent("Unload Cached", "씬 전환을 기다리지 않고 대기(Cached) 애셋을 지금 해제합니다."), EditorStyles.toolbarButton))
                    {
                        var count = ContentSystem.Lifetime.UnloadCached();
                        Debug.Log($"[Contents Loader] 대기 상태 애셋 {count}개를 해제했습니다.");
                        Refresh();
                    }
                }
            }
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var unlimited = budget <= 0 || budget == long.MaxValue;
                var rect = EditorGUILayout.GetControlRect(false, 20);
                var label = unlimited
                    ? $"Memory {FormatSize(loadedSize)}  (budget: unlimited)"
                    : $"Memory {FormatSize(loadedSize)} / {FormatSize(budget)}  ({loadedSize * 100.0 / budget:0}%)";
                EditorGUI.ProgressBar(rect, unlimited ? 0f : Mathf.Clamp01((float)loadedSize / budget), label);

                using (new EditorGUILayout.HorizontalScope())
                {
                    StateBadge(LoadedColor, $"Loaded {loadedCount}");
                    StateBadge(CachedColor, $"Cached {cachedCount}");
                    StateBadge(LoadingColor, $"Loading {loadingCount}");
                    GUILayout.FlexibleSpace();
                    if (packs.Count > 0)
                        GUILayout.Label($"Packs open {packs.Count(p => p.RefCount > 0)} / {packs.Count}", EditorStyles.miniLabel);
                    GUILayout.Label(backendName, EditorStyles.miniLabel);
                }
            }
        }

        private void StateBadge(Color color, string text)
        {
            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label("●", dot, GUILayout.Width(12));
            GUI.contentColor = old;
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(70));
        }

        // Assets 탭

        private static readonly (AssetColumn Column, string Title, float Width)[] AssetColumns =
        {
            (AssetColumn.State, "State", 70), (AssetColumn.Path, "Path", -1), (AssetColumn.Type, "Type", 110),
            (AssetColumn.Owner, "Owner", 140), (AssetColumn.Size, "Size", 72), (AssetColumn.LastUsed, "Last Used", 72),
        };

        private void DrawAssets()
        {
            IEnumerable<AssetRow> rows = assets;
            if (stateFilter != StateFilter.All)
            {
                var state = stateFilter switch
                {
                    StateFilter.Loading => ContentState.Loading,
                    StateFilter.Cached => ContentState.Cached,
                    _ => ContentState.Loaded,
                };
                rows = rows.Where(r => r.State == state);
            }
            if (!string.IsNullOrEmpty(search))
                rows = rows.Where(r => Contains(r.Path, search) || Contains(r.Type.Name, search) || Contains(r.Owner, search));

            Func<AssetRow, IComparable> key = assetSort switch
            {
                AssetColumn.State => r => r.State,
                AssetColumn.Type => r => r.Type.Name,
                AssetColumn.Owner => r => r.Owner,
                AssetColumn.Size => r => r.Size,
                AssetColumn.LastUsed => r => r.FramesAgo,
                _ => r => r.Path,
            };
            var list = (sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenBy(r => r.Path).ToList();

            var widths = ColumnWidths(AssetColumns.Select(c => c.Width).ToArray());
            var header = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            DrawHeader(header, widths, AssetColumns.Select(c => c.Title).ToArray(), (int)assetSort, i =>
            {
                ToggleSort(ref assetSort, AssetColumns[i].Column);
            });

            if (list.Count == 0)
            {
                EditorGUILayout.LabelField(assets.Count == 0 ? "로드된 콘텐츠가 없습니다." : "조건에 맞는 콘텐츠가 없습니다.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            var view = GUILayoutUtility.GetRect(0, list.Count * RowHeight, GUILayout.ExpandWidth(true));
            for (var i = 0; i < list.Count; i++)
            {
                var rect = new Rect(view.x, view.y + i * RowHeight, view.width, RowHeight);
                if (rect.yMax < scroll.y || rect.y > scroll.y + position.height) continue;
                DrawAssetRow(rect, widths, list[i], i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetRow(Rect rect, float[] widths, AssetRow row, int index)
        {
            DrawRowBackground(rect, index);
            var cells = Cells(rect, widths);

            DrawState(cells[0], StateColor(row.State), row.State.ToString());
            GUI.Label(cells[1], new GUIContent(row.Path, row.Path));
            GUI.Label(cells[2], new GUIContent(row.Type.Name, row.Type.FullName));
            GUI.Label(cells[3], new GUIContent(row.Owner, row.Owner));
            GUI.Label(cells[4], row.State == ContentState.Loading ? "-" : FormatSize(row.Size), rightLabel);
            GUI.Label(cells[5], row.FramesAgo <= 0 ? "now" : $"{row.FramesAgo} f", rightLabel);

            HandleRowClick(rect, () => Ping(row.Path, row.Type), () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Ping Asset"), false, () => Ping(row.Path, row.Type));
                menu.AddItem(new GUIContent("Copy Path"), false, () => EditorGUIUtility.systemCopyBuffer = row.Path);
                menu.ShowAsContext();
            });
        }

        private static Color StateColor(ContentState state) => state switch
        {
            ContentState.Loading => LoadingColor,
            ContentState.Loaded => LoadedColor,
            ContentState.Cached => CachedColor,
            _ => IdleColor,
        };

        // Packs 탭

        private static readonly (PackColumn Column, string Title, float Width)[] PackColumns =
        {
            (PackColumn.Pack, "Pack", -1), (PackColumn.Delivery, "Delivery", 72), (PackColumn.Location, "On Device", 100),
            (PackColumn.Size, "Size", 72), (PackColumn.Memory, "Memory", 72), (PackColumn.Refs, "Refs", 48), (PackColumn.Assets, "Assets", 56),
        };

        private void DrawPacks()
        {
            if (backendName != "AssetBundles")
            {
                EditorGUILayout.HelpBox($"{backendName} 모드라 팩이 없습니다. 팩 상태를 보려면 Contents Settings의 Play Mode Source를 Bundles로 바꾸세요.", MessageType.Info);
                return;
            }

            IEnumerable<PackInfo> rows = packs;
            if (openPacksOnly) rows = rows.Where(p => p.RefCount > 0 || p.Opening);
            if (!string.IsNullOrEmpty(search))
                rows = rows.Where(p => Contains(p.FileName, search) || p.Assets.Any(a => Contains(a, search)));

            Func<PackInfo, IComparable> key = packSort switch
            {
                PackColumn.Delivery => p => p.Delivery,
                PackColumn.Location => p => p.Location,
                PackColumn.Size => p => p.Size,
                PackColumn.Memory => p => p.LoadedBytes,
                PackColumn.Refs => p => p.RefCount,
                PackColumn.Assets => p => p.Assets.Length,
                _ => p => p.FileName,
            };
            var list = (sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenBy(p => p.FileName).ToList();

            var widths = ColumnWidths(PackColumns.Select(c => c.Width).ToArray());
            var header = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            DrawHeader(header, widths, PackColumns.Select(c => c.Title).ToArray(), (int)packSort, i =>
            {
                ToggleSort(ref packSort, PackColumns[i].Column);
            });

            scroll = EditorGUILayout.BeginScrollView(scroll);
            var index = 0;
            foreach (var pack in list)
            {
                var rect = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
                DrawPackRow(rect, widths, pack, index++);
                if (!expandedPacks.Contains(pack.FileName)) continue;
                foreach (var asset in pack.Assets.OrderBy(a => a))
                {
                    var assetRect = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
                    DrawRowBackground(assetRect, index++);
                    var labelRect = new Rect(assetRect.x + 34, assetRect.y, assetRect.width - 34, RowHeight);
                    GUI.Label(labelRect, new GUIContent(asset, EditorGUIUtility.ObjectContent(null, AssetDatabase.GetMainAssetTypeAtPath(asset)).image, asset));
                    HandleRowClick(assetRect, () => PingAssetPath(asset), null);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPackRow(Rect rect, float[] widths, PackInfo pack, int index)
        {
            DrawRowBackground(rect, index);
            var cells = Cells(rect, widths);

            var expanded = expandedPacks.Contains(pack.FileName);
            var foldRect = new Rect(cells[0].x, cells[0].y, 14, RowHeight);
            var newExpanded = pack.Assets.Length > 0 && EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
            if (newExpanded != expanded)
            {
                if (newExpanded) expandedPacks.Add(pack.FileName);
                else expandedPacks.Remove(pack.FileName);
            }

            var open = pack.RefCount > 0 || pack.Opening;
            var nameRect = new Rect(cells[0].x + 14, cells[0].y, cells[0].width - 14, RowHeight);
            DrawState(nameRect, pack.Opening ? LoadingColor : open ? LoadedColor : IdleColor, PackId(pack.FileName), pack.FileName);
            GUI.Label(cells[1], pack.Delivery.ToString());
            GUI.Label(cells[2], pack.Location switch
            {
                PackLocation.BuiltIn => "Built-in",
                PackLocation.Downloaded => "Downloaded",
                _ => "Not downloaded",
            });
            GUI.Label(cells[3], FormatSize(pack.Size), rightLabel);
            GUI.Label(cells[4], open ? FormatSize(pack.LoadedBytes) : "-", rightLabel);
            GUI.Label(cells[5], pack.Opening ? "…" : pack.RefCount.ToString(), rightLabel);
            GUI.Label(cells[6], pack.Assets.Length.ToString(), rightLabel);

            HandleRowClick(rect, null, () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy File Name"), false, () => EditorGUIUtility.systemCopyBuffer = pack.FileName);
                menu.ShowAsContext();
            });
        }

        // Settings 탭

        private void DrawSettings()
        {
            var settings = ContentsSettings.Find();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("ContentsSettings 애셋이 없어서 기본값으로 동작 중입니다.", MessageType.Info);
                if (GUILayout.Button("Create Settings", GUILayout.Width(160)))
                    Selection.activeObject = ContentsSettings.GetOrCreate();
                return;
            }

            if (settingsEditor == null || settingsEditor.target != settings)
            {
                if (settingsEditor != null) DestroyImmediate(settingsEditor);
                settingsEditor = UnityEditor.Editor.CreateEditor(settings);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(8, 8, 6, 6) }))
            {
                if (EditorApplication.isPlaying)
                    EditorGUILayout.HelpBox("플레이 중 바꾼 값은 다음 플레이나 다음 콘텐츠 빌드부터 적용됩니다.", MessageType.None);

                var labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Max(200, position.width * 0.4f);
                settingsEditor.OnInspectorGUI();
                EditorGUIUtility.labelWidth = labelWidth;

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Folder Rules", EditorStyles.boldLabel);
                DrawFolderRules(settings);

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
                DrawBuildButtons();
            }
            EditorGUILayout.EndScrollView();
        }

        // 규칙이 없는 Contents 폴더는 DefaultDelivery를 따름
        private static void DrawFolderRules(ContentsSettings settings)
        {
            if (settings.FolderRules.Count == 0)
            {
                EditorGUILayout.LabelField($"폴더 규칙이 없습니다. 모든 Contents 폴더가 {settings.DefaultDelivery}입니다. 폴더를 선택해 인스펙터에서 규칙을 추가하세요.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (var rule in settings.FolderRules.ToList())
            {
                var path = AssetDatabase.GUIDToAssetPath(rule.Guid);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var folder = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false);
                    if (folder == null) GUILayout.Label("(missing)", EditorStyles.miniLabel, GUILayout.Width(56));

                    var delivery = (ContentDelivery)EditorGUILayout.EnumPopup(rule.Delivery, GUILayout.Width(90));
                    if (delivery != rule.Delivery)
                    {
                        Undo.RecordObject(settings, "Change Folder Delivery");
                        settings.SetRule(rule.Guid, delivery);
                    }
                    if (GUILayout.Button(new GUIContent("✕", "규칙을 지우고 상위 폴더나 기본값을 따르게 합니다."), GUILayout.Width(24)))
                    {
                        Undo.RecordObject(settings, "Remove Folder Rule");
                        settings.SetRule(rule.Guid, null);
                    }
                }
            }
        }

        private void DrawBuildButtons()
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            var playMode = ContentsBuildPaths.PlayModeTarget;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Build Content ({active})")) EditorApplication.delayCall += () => ContentsBuilder.BuildContent(active);
                if (playMode != active && GUILayout.Button($"Build for Play Mode ({playMode})"))
                    EditorApplication.delayCall += () => ContentsBuilder.BuildForPlayMode();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var root = ContentsBuildPaths.Root(active);
                using (new EditorGUI.DisabledScope(!System.IO.Directory.Exists(root)))
                {
                    if (GUILayout.Button("Show Build Folder")) EditorUtility.RevealInFinder(root);
                }
                var report = ContentsBuildPaths.Report(active);
                using (new EditorGUI.DisabledScope(!System.IO.File.Exists(report)))
                {
                    if (GUILayout.Button("Open Build Report")) EditorUtility.OpenWithDefaultApp(System.IO.Path.GetFullPath(report));
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Clear Play Mode Download Cache"))
                        EditorApplication.ExecuteMenuItem("Tools/Contents Loader/Clear Play Mode Download Cache");
                }
            }
        }

        // 파일 이름은 "팩ID_해시.bundle"
        private static string PackId(string fileName)
        {
            var underscore = fileName.IndexOf('_');
            return underscore > 0 ? fileName.Substring(0, underscore) : fileName;
        }

        // 공통 그리기

        private float[] ColumnWidths(float[] fixedWidths)
        {
            var width = position.width - 16;
            var flexible = Mathf.Max(120, width - fixedWidths.Where(w => w > 0).Sum());
            return fixedWidths.Select(w => w > 0 ? w : flexible).ToArray();
        }

        private static Rect[] Cells(Rect row, float[] widths)
        {
            var cells = new Rect[widths.Length];
            var x = row.x + 4;
            for (var i = 0; i < widths.Length; i++)
            {
                cells[i] = new Rect(x, row.y, widths[i] - 6, row.height);
                x += widths[i];
            }
            return cells;
        }

        private void DrawHeader(Rect rect, float[] widths, string[] titles, int sorted, Action<int> onClick)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
            var cells = Cells(rect, widths);
            for (var i = 0; i < titles.Length; i++)
            {
                var title = i == sorted ? $"{titles[i]} {(sortDescending ? "▼" : "▲")}" : titles[i];
                if (GUI.Button(cells[i], title, EditorStyles.miniBoldLabel)) onClick(i);
            }
        }

        private void ToggleSort<T>(ref T current, T column) where T : Enum
        {
            if (current.Equals(column)) sortDescending = !sortDescending;
            else
            {
                current = column;
                sortDescending = false;
            }
        }

        private void DrawState(Rect rect, Color color, string text, string tooltip = null)
        {
            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(rect.x, rect.y, 12, rect.height), "●", dot);
            GUI.contentColor = old;
            GUI.Label(new Rect(rect.x + 14, rect.y, rect.width - 14, rect.height), new GUIContent(text, tooltip ?? text));
        }

        private static void DrawRowBackground(Rect rect, int index)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (index % 2 == 1) EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.03f) : new Color(0, 0, 0, 0.04f));
            if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 0.9f, 0.12f));
        }

        private static void HandleRowClick(Rect rect, Action onDoubleClick, Action onContext)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && onDoubleClick != null)
            {
                onDoubleClick();
                e.Use();
            }
            else if (e.type == EventType.ContextClick && onContext != null)
            {
                onContext();
                e.Use();
            }
        }

        private void Ping(string contentPath, Type type)
        {
            assetPathsOfContent ??= ContentsScanner.ScanProject().Entries
                .GroupBy(e => ContentPath.Normalize(e.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => e.AssetPath).ToList(), StringComparer.OrdinalIgnoreCase);
            if (!assetPathsOfContent.TryGetValue(contentPath, out var candidates)) return;
            var path = candidates.FirstOrDefault(p => AssetDatabase.LoadAssetAtPath(p, type) != null) ?? candidates[0];
            PingAssetPath(path);
        }

        private static void PingAssetPath(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null) return;
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private static bool Contains(string text, string part) => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }
    }
}
