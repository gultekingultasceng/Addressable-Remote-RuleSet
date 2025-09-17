using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Digitoy
{

    public class SmartAddressablesBuilder : EditorWindow
    {
        private const string DefaultGroupName = "Remote_Content";
        private const string DefaultLabel = "cdn";

        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private string _groupName = DefaultGroupName;
        private string _label = DefaultLabel;
        private bool _includeSubAssets = true;
        private bool _addPlatformLabel = true;
        private bool _prefixAddressWithFolder = false;
        private string _outputRoot = "C:/Users/Gultekin/Desktop/AdressablesFolder";
        private Vector2 _scroll;
        // Versioning
        private bool _useVersionFolder = false;
        private bool _autoIncrementVersion = true;
        private string _versionPrefix = "v";
        private int _versionPadding = 3;
        private string _customVersion = "";
        private bool _useTimestampVersion = false;
        private string _timestampFormat = "yyyyMMdd_HHmmss";
        private bool _forceOverrideProfilePaths = true;
        // Content Update
        private string _contentStateFile = "Assets/AddressableAssetsData/AddressablesContentState.bin";
        // Remote Copy
        private bool _alsoCopyToRemote = false;
        private string _remoteDestRoot = ""; // e.g., \\\\server\\share\\AddressablesRoot or local folder

        [MenuItem("Tools/Addressables/Smart Builder")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<SmartAddressablesBuilder>(false, "Smart Addr Builder");
            wnd.minSize = new Vector2(520, 460);
            wnd.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Smart Addressables Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 1 — Select Assets", EditorStyles.boldLabel);
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(160)))
                {
                    _scroll = scroll.scrollPosition;
                    for (int i = 0; i < _assets.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            _assets[i] = EditorGUILayout.ObjectField(_assets[i], typeof(UnityEngine.Object), false);
                            if (GUILayout.Button("X", GUILayout.Width(24)))
                            {
                                _assets.RemoveAt(i);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }

                var dropRect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
                GUI.Box(dropRect, "Drop assets or folders here", EditorStyles.helpBox);
                var evt = Event.current;
                if (dropRect.Contains(evt.mousePosition))
                {
                    if (evt.type == EventType.DragUpdated)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    else if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj != null && !_assets.Contains(obj)) _assets.Add(obj);
                        }
                        evt.Use();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Use Current Selection"))
                    {
                        var toAdd = Selection.objects.Where(o => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o)));
                        _assets.AddRange(toAdd.Except(_assets));
                    }
                    if (GUILayout.Button("Clear")) _assets.Clear();
                }

                _includeSubAssets = EditorGUILayout.ToggleLeft("Include folder children (recursive)", _includeSubAssets);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 2 — Build Settings", EditorStyles.boldLabel);
                _groupName = EditorGUILayout.TextField("Group Name", _groupName);
                _label = EditorGUILayout.TextField("Label", _label);
                _outputRoot = EditorGUILayout.TextField("Output Root Folder", _outputRoot);
                _addPlatformLabel = EditorGUILayout.ToggleLeft("Add platform label to entries (StandaloneWindows64/Android/iOS)", _addPlatformLabel);
                _prefixAddressWithFolder = EditorGUILayout.ToggleLeft("Prefix address with folder names", _prefixAddressWithFolder);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Versioning", EditorStyles.boldLabel);
                _useVersionFolder = EditorGUILayout.ToggleLeft("Use versioned subfolder (v001, v002, ...)", _useVersionFolder);
                if (_useVersionFolder)
                {
                    _autoIncrementVersion = EditorGUILayout.ToggleLeft("Auto-increment version", _autoIncrementVersion);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _versionPrefix = EditorGUILayout.TextField("Version Prefix", _versionPrefix);
                        _versionPadding = EditorGUILayout.IntField("Padding", _versionPadding);
                    }
                    _useTimestampVersion = EditorGUILayout.ToggleLeft("Use timestamp instead of numeric", _useTimestampVersion);
                    if (_useTimestampVersion)
                    {
                        _timestampFormat = EditorGUILayout.TextField("Timestamp Format", _timestampFormat);
                    }
                    if (!_autoIncrementVersion)
                    {
                        _customVersion = EditorGUILayout.TextField("Custom Version (e.g., v105)", _customVersion);
                    }
                }
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Remote Copy (optional)", EditorStyles.boldLabel);
                _alsoCopyToRemote = EditorGUILayout.ToggleLeft("Also copy last build to remote destination", _alsoCopyToRemote);
                if (_alsoCopyToRemote)
                {
                    _remoteDestRoot = EditorGUILayout.TextField("Remote Dest Root", _remoteDestRoot);
                }
                _forceOverrideProfilePaths = EditorGUILayout.ToggleLeft("Force override Remote Build/Load Path in profile", _forceOverrideProfilePaths);

                var btName = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
                var platformRoot = Path.Combine(_outputRoot.Replace('\\', '/'), "ServerData", btName);
                var previewBuildFolder = platformRoot;
                if (_useVersionFolder)
                {
                    string nextVer;
                    if (_useTimestampVersion)
                    {
                        nextVer = ComposeTimestampVersion(_versionPrefix, _timestampFormat);
                    }
                    else
                    {
                        nextVer = _autoIncrementVersion ? GetNextVersionName(platformRoot, _versionPrefix, _versionPadding) : _customVersion;
                    }
                    if (string.IsNullOrEmpty(nextVer)) nextVer = _versionPrefix + 1.ToString().PadLeft(_versionPadding, '0');
                    previewBuildFolder = Path.Combine(platformRoot, nextVer);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Build Folder:", previewBuildFolder);
                    if (GUILayout.Button("Open Folder", GUILayout.Width(120)))
                    {
                        if (!Directory.Exists(previewBuildFolder)) Directory.CreateDirectory(previewBuildFolder);
                        EditorUtility.RevealInFinder(previewBuildFolder);
                    }
                }
            }

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Prepare + Build (Export)", GUILayout.Height(36)))
                {
                    BuildAndExport();
                }
                if (GUILayout.Button("Only Generate Manifest", GUILayout.Height(36)))
                {
                    TryWriteAddressesManifest();
                }
            }

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Content Update", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _contentStateFile = EditorGUILayout.TextField("Content State File", _contentStateFile);
                    if (GUILayout.Button("...", GUILayout.Width(32)))
                    {
                        var picked = EditorUtility.OpenFilePanel("Select AddressablesContentState.bin", Application.dataPath, "bin");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            var projRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
                            if (picked.StartsWith(projRoot)) picked = "Assets" + picked.Substring(projRoot.Length).Replace('\\', '/');
                            _contentStateFile = picked;
                        }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Prepare Content Update"))
                    {
                        PrepareContentUpdate();
                    }
                    if (GUILayout.Button("Build Content Update"))
                    {
                        BuildContentUpdate();
                    }
                }
            }
        }

        private void BuildAndExport()
        {
            if (_assets.Count == 0)
            {
                EditorUtility.DisplayDialog("No Assets", "Lütfen en az bir asset seçin.", "OK");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) { EditorUtility.DisplayDialog("Error", "AddressableAssetSettings bulunamadı.", "OK"); return; }

            EnsurePlayModeBuilder(settings);
            EnsureActiveProfile(settings);
            var group = EnsureGroup(settings, _groupName);
            var schema = EnsureBundledSchema(settings, group);

            var prof = settings.profileSettings;
            var outRoot = _outputRoot.Replace('\\', '/');
            if (string.IsNullOrEmpty(outRoot)) { EditorUtility.DisplayDialog("Error", "Output Root Folder boş.", "OK"); return; }
            var platformRoot = outRoot.TrimEnd('/', '\\') + "/ServerData/[BuildTarget]";
            var buildFolder = platformRoot;
            string versionSegment = string.Empty;
            if (_useVersionFolder)
            {
                var platformRootAbs = ResolveProfileString(settings, platformRoot);
                string nextVersion;
                if (_useTimestampVersion)
                {
                    nextVersion = ComposeTimestampVersion(_versionPrefix, _timestampFormat);
                }
                else
                {
                    nextVersion = _autoIncrementVersion ? GetNextVersionName(platformRootAbs, _versionPrefix, _versionPadding) : _customVersion;
                }
                if (string.IsNullOrEmpty(nextVersion)) nextVersion = _versionPrefix + 1.ToString().PadLeft(_versionPadding, '0');
                versionSegment = "/" + nextVersion;
                buildFolder = platformRoot + versionSegment;
            }
            var remoteBuildDefault = buildFolder;
            if (_forceOverrideProfilePaths)
            {
                try { prof.SetValue(settings.activeProfileId, "RemoteBuildPath", remoteBuildDefault); } catch { EnsureProfileVar(prof, settings.activeProfileId, "RemoteBuildPath", remoteBuildDefault); }
                var defaultLoadRoot = "http://localhost:8000/ServerData/[BuildTarget]";
                var remoteLoadDefault = defaultLoadRoot + versionSegment;
                try { prof.SetValue(settings.activeProfileId, "RemoteLoadPath", remoteLoadDefault); } catch { EnsureProfileVar(prof, settings.activeProfileId, "RemoteLoadPath", remoteLoadDefault); }
            }
            else
            {
                EnsureProfileVar(prof, settings.activeProfileId, "RemoteBuildPath", remoteBuildDefault);
                var defaultLoadRoot = "http://localhost:8000/ServerData/[BuildTarget]";
                var remoteLoadDefault = defaultLoadRoot + versionSegment;
                EnsureProfileVar(prof, settings.activeProfileId, "RemoteLoadPath", remoteLoadDefault);
            }

            var btName = GetBuildTargetNameSafe(settings);

            var guids = ExpandToGuids(_assets, _includeSubAssets);
            foreach (var guid in guids)
            {
                var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                if (entry == null) continue;

                if (string.IsNullOrEmpty(entry.address))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var baseName = Path.GetFileNameWithoutExtension(path);
                    if (_prefixAddressWithFolder)
                    {
                        var dir = Path.GetDirectoryName(path).Replace('\\', '/');
                        // Trim leading Assets/
                        if (dir.StartsWith("Assets/")) dir = dir.Substring("Assets/".Length);
                        entry.address = string.IsNullOrEmpty(dir) ? baseName : dir + "/" + baseName;
                    }
                    else
                    {
                        entry.address = baseName;
                    }
                }

                if (!string.IsNullOrEmpty(_label)) entry.SetLabel(_label, true, true);
                if (_addPlatformLabel) entry.SetLabel(btName, true, true);
            }

            schema.BuildPath.SetVariableByName(settings, "RemoteBuildPath");
            schema.LoadPath.SetVariableByName(settings, "RemoteLoadPath");
            schema.IncludeInBuild = true;

            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, "RemoteBuildPath");
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, "RemoteLoadPath");

            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            AddressableAssetSettings.BuildPlayerContent();

            var evaluatedBuildPath = settings.profileSettings.EvaluateString(settings.activeProfileId, "{RemoteBuildPath}");
            // Marker at platform root should point to version subfolder file if versioning is on
            try
            {
                var platformRootAbs = ResolveProfileString(settings, platformRoot);
                if (_useVersionFolder)
                    WriteLatestCatalogMarkerAtRoot(platformRootAbs, evaluatedBuildPath);
                else
                    WriteLatestCatalogMarker(evaluatedBuildPath);
            }
            catch (Exception ex) { Debug.LogWarning(ex.Message); }
            try { WriteAddressesManifest(evaluatedBuildPath, settings, guids); } catch (Exception ex) { Debug.LogWarning(ex.Message); }

            // Optional remote copy
            if (_alsoCopyToRemote && !string.IsNullOrEmpty(_remoteDestRoot))
            {
                try
                {
                    var destPlatformRoot = Path.Combine(_remoteDestRoot, "ServerData", btName).Replace('\\', '/');
                    Directory.CreateDirectory(destPlatformRoot);
                    CopyDirectory(evaluatedBuildPath, Path.Combine(destPlatformRoot, new DirectoryInfo(evaluatedBuildPath).Name));
                    if (_useVersionFolder)
                        WriteLatestCatalogMarkerAtRoot(destPlatformRoot, Path.Combine(destPlatformRoot, new DirectoryInfo(evaluatedBuildPath).Name));
                    else
                        WriteLatestCatalogMarker(destPlatformRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Remote copy failed: " + ex.Message);
                }
            }

            EditorUtility.DisplayDialog("Done", "Smart build/export tamam.", "OK");
        }

        private void TryWriteAddressesManifest()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null) { EditorUtility.DisplayDialog("Error", "AddressableAssetSettings yok.", "OK"); return; }
            var prof = settings.profileSettings;
            var evaluatedBuildPath = prof.EvaluateString(settings.activeProfileId, "{RemoteBuildPath}");
            var guids = ExpandToGuids(_assets, _includeSubAssets);
            WriteAddressesManifest(evaluatedBuildPath, settings, guids);
            EditorUtility.DisplayDialog("Done", "addresses_manifest.json yazıldı.", "OK");
        }

        private static void EnsurePlayModeBuilder(AddressableAssetSettings settings)
        {
            int index = -1;
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                var db = settings.DataBuilders[i];
                if (db == null) continue;
                var typeName = db.GetType().Name;
                var assetName = db.name;
                if (typeName.Contains("BuildScriptPackedPlayMode") || assetName.Contains("BuildScriptPackedPlayMode"))
                {
                    index = i; break;
                }
            }
            if (index >= 0) settings.ActivePlayModeDataBuilderIndex = index;
        }

        private static void EnsureActiveProfile(AddressableAssetSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.activeProfileId)) return;
            try { var id = settings.profileSettings.GetProfileId("Default"); if (!string.IsNullOrEmpty(id)) settings.activeProfileId = id; } catch { }
        }

        private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string groupName)
        {
            var group = settings.FindGroup(groupName) ?? settings.CreateGroup(groupName, false, false, false, new List<AddressableAssetGroupSchema>());
            var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.UseAssetBundleCache = true;
            schema.UseAssetBundleCrc = true;
            schema.UseUnityWebRequestForLocalBundles = true;
            schema.IncludeInBuild = true;
            return group;
        }

        private static BundledAssetGroupSchema EnsureBundledSchema(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.UseAssetBundleCache = true;
            schema.UseAssetBundleCrc = true;
            schema.UseUnityWebRequestForLocalBundles = true;
            schema.BuildPath.SetVariableByName(settings, "RemoteBuildPath");
            schema.LoadPath.SetVariableByName(settings, "RemoteLoadPath");
            schema.IncludeInBuild = true;
            return schema;
        }

        private static void EnsureProfileVar(AddressableAssetProfileSettings prof, string profileId, string varName, string defaultValue)
        {
            string current = null;
            try { current = prof.GetValueByName(profileId, varName); } catch { }
            if (string.IsNullOrEmpty(current))
            {
                try { prof.CreateValue(varName, defaultValue); } catch { }
                try { prof.SetValue(profileId, varName, defaultValue); } catch { }
            }
        }

        private static IEnumerable<string> ExpandToGuids(IEnumerable<UnityEngine.Object> objs, bool includeChildren)
        {
            var set = new HashSet<string>();
            foreach (var o in objs)
            {
                if (o == null) continue;
                var assetPath = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    if (includeChildren)
                    {
                        foreach (var p in EnumerateFilesRecursive(assetPath))
                        {
                            var g = AssetDatabase.AssetPathToGUID(p);
                            if (!string.IsNullOrEmpty(g)) set.Add(g);
                        }
                    }
                }
                else
                {
                    var guid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (!string.IsNullOrEmpty(guid)) set.Add(guid);
                }
            }
            return set;
        }

        private static IEnumerable<string> EnumerateFilesRecursive(string folderAssetPath)
        {
            var stack = new Stack<string>();
            stack.Push(folderAssetPath);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { current }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        if (path != current) stack.Push(path);
                    }
                    else
                    {
                        yield return path;
                    }
                }
            }
        }

        private static string GetBuildTargetNameSafe(AddressableAssetSettings settings)
        {
            try
            {
                var name = settings.profileSettings.EvaluateString(settings.activeProfileId, "{BuildTarget}");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
        }

        private static void WriteLatestCatalogMarker(string targetFolder)
        {
            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder)) return;
            var json = FindLatestCatalogJson(targetFolder);
            if (string.IsNullOrEmpty(json)) return;
            File.WriteAllText(Path.Combine(targetFolder, "latest_catalog.txt"), json);
        }

        private static string FindLatestCatalogJson(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var jsons = Directory.GetFiles(folder, "catalog_*.json", SearchOption.TopDirectoryOnly);
            if (jsons == null || jsons.Length == 0) return null;
            return jsons.Select(p => new FileInfo(p)).OrderByDescending(fi => fi.LastWriteTimeUtc).First().Name;
        }

        private static void WriteLatestCatalogMarkerAtRoot(string platformRootFolder, string versionFolder)
        {
            if (string.IsNullOrEmpty(platformRootFolder) || string.IsNullOrEmpty(versionFolder)) return;
            if (!Directory.Exists(platformRootFolder) || !Directory.Exists(versionFolder)) return;
            var latestJson = FindLatestCatalogJson(versionFolder);
            if (string.IsNullOrEmpty(latestJson)) return;
            var rel = MakeRelativePath(platformRootFolder, Path.Combine(versionFolder, latestJson));
            File.WriteAllText(Path.Combine(platformRootFolder, "latest_catalog.txt"), rel.Replace('\\', '/'));
        }

        private static string MakeRelativePath(string fromPath, string toPath)
        {
            var fromUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(fromPath)));
            var toUri = new Uri(Path.GetFullPath(toPath));
            return Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString())) return path + Path.DirectorySeparatorChar;
            return path;
        }

        private static string ResolveProfileString(AddressableAssetSettings settings, string valueWithVars)
        {
            return settings.profileSettings.EvaluateString(settings.activeProfileId, valueWithVars);
        }

        private static string GetNextVersionName(string platformRootAbs, string prefix, int padding)
        {
            try
            {
                if (string.IsNullOrEmpty(platformRootAbs)) return null;
                Directory.CreateDirectory(platformRootAbs);
                var max = 0;
                foreach (var dir in Directory.GetDirectories(platformRootAbs))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(prefix))
                    {
                        var numStr = name.Substring(prefix.Length);
                        if (int.TryParse(numStr, out var n)) max = Mathf.Max(max, n);
                    }
                }
                var next = max + 1;
                return prefix + next.ToString().PadLeft(padding, '0');
            }
            catch { return null; }
        }

        private static string ComposeTimestampVersion(string prefix, string format)
        {
            try
            {
                var stamp = DateTime.Now.ToString(string.IsNullOrEmpty(format) ? "yyyyMMdd_HHmmss" : format);
                return string.IsNullOrEmpty(prefix) ? stamp : prefix + stamp;
            }
            catch
            {
                return prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }
        }

        private void PrepareContentUpdate()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null) { EditorUtility.DisplayDialog("Error", "AddressableAssetSettings yok.", "OK"); return; }
            var path = _contentStateFile;
            if (!File.Exists(path))
            {
                // try convert relative Assets path
                if (path.StartsWith("Assets")) path = Path.GetFullPath(path);
            }
            if (!File.Exists(path)) { EditorUtility.DisplayDialog("Error", "Content State dosyası bulunamadı.", "OK"); return; }
            try
            {
                if (!TryInvokeContentUpdate("PrepareContentUpdate", settings, path, out var err))
                    throw new System.Exception(err ?? "ContentUpdateScript bulunamadı.");
                EditorUtility.DisplayDialog("Content Update", "Prepare tamamlandı. Değişenler Content Update grubuna taşındı.", "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        private void BuildContentUpdate()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null) { EditorUtility.DisplayDialog("Error", "AddressableAssetSettings yok.", "OK"); return; }
            var path = _contentStateFile;
            if (!File.Exists(path))
            {
                if (path.StartsWith("Assets")) path = Path.GetFullPath(path);
            }
            if (!File.Exists(path)) { EditorUtility.DisplayDialog("Error", "Content State dosyası bulunamadı.", "OK"); return; }
            try
            {
                if (!TryInvokeContentUpdate("BuildContentUpdate", settings, path, out var err))
                    throw new System.Exception(err ?? "ContentUpdateScript bulunamadı.");
                EditorUtility.DisplayDialog("Content Update", "Build Content Update tamamlandı.", "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        private static bool TryInvokeContentUpdate(string methodName, AddressableAssetSettings settings, string contentStatePath, out string error)
        {
            error = null;
            try
            {
                var type = FindContentUpdateScriptType();
                if (type == null)
                {
                    error = "ContentUpdateScript type not found in current Addressables version.";
                    return false;
                }
                var mi = type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(AddressableAssetSettings), typeof(string) }, null);
                if (mi == null)
                {
                    error = $"Method {methodName}(AddressableAssetSettings, string) not found on ContentUpdateScript.";
                    return false;
                }
                mi.Invoke(null, new object[] { settings, contentStatePath });
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static System.Type FindContentUpdateScriptType()
        {
            var candidates = new[]
            {
            "UnityEditor.AddressableAssets.Build.ContentUpdate.ContentUpdateScript",
            "UnityEditor.AddressableAssets.Build.ContentUpdateScript",
        };
            foreach (var name in candidates)
            {
                var t = System.Type.GetType(name);
                if (t != null) return t;
            }
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type t = null;
                try
                {
                    t = asm.GetType("UnityEditor.AddressableAssets.Build.ContentUpdate.ContentUpdateScript")
                        ?? asm.GetType("UnityEditor.AddressableAssets.Build.ContentUpdateScript");
                }
                catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(destDir)) return;
            if (!Directory.Exists(sourceDir)) return;
            foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));
            }
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(sourceDir, destDir);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
            }
        }

        private static void WriteAddressesManifest(string evaluatedBuildPath, AddressableAssetSettings settings, IEnumerable<string> guids)
        {
            try
            {
                if (string.IsNullOrEmpty(evaluatedBuildPath)) return;
                Directory.CreateDirectory(evaluatedBuildPath);
                var list = new List<object>();
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var entry = settings.FindAssetEntry(g);
                    if (entry == null) continue;
                    list.Add(new
                    {
                        address = entry.address,
                        guid = g,
                        path,
                        labels = entry.labels?.ToArray() ?? Array.Empty<string>()
                    });
                }
                var json = JsonUtility.ToJson(new Wrapper { items = list.ToArray() }, true);
                File.WriteAllText(Path.Combine(evaluatedBuildPath, "addresses_manifest.json"), json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("addresses_manifest.json yazılamadı: " + ex.Message);
            }
        }

        [Serializable]
        private class Wrapper { public object[] items; }
    }
}