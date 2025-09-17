using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement;
using UnityEngine.AddressableAssets.ResourceLocators;

namespace Digitoy
{
    public class AddressableLoader : MonoBehaviour
    {
        [Header("Catalog Source (Simple)")]
        [Tooltip("Base URL root. Example: http://127.0.0.1:8000/ServerData")]
        public string baseUrlRoot = "http://localhost:8000/ServerData";

        [Tooltip("If true, appends /{PlatformName}. Example: /StandaloneWindows64")]
        private bool appendPlatformFolder = true;

        [Header("Version Selection")]
        [Tooltip("Always pick the latest version folder under the platform.")]
        private bool pickLatestVersion = true;

        [Header("Instantiate Options")]
        [Tooltip("Optional parent for instantiated GameObjects. If null, instantiates at root.")]
        public Transform parent;

        [Tooltip("If parent is null, try to find a Canvas in scene and use it as parent.")]
        public bool tryFindCanvasAsParent = true;

        [Header("What To Load")]
        [Tooltip("Load all assets with this label. Default: cdn")]
        public string labelToLoad = "cdn";

        [Tooltip("Instantiate loaded GameObjects")]
        public bool instantiate = true;

        [Header("Resilience")]
        [Tooltip("How many times to retry catalog resolve/load on transient errors")]
        public int retryCount = 2;

        [Tooltip("Seconds between retries (exponential backoff factor applies)")]
        public float retryDelaySeconds = 0.75f;

        [Header("Platform Label Filter")]
        [Tooltip("Also require the current platform label when loading by label (e.g., StandaloneWindows64)")]
        private bool intersectWithPlatformLabel = false;

        [Header("Catalog Lifecycle")]
        [Tooltip("Remove previously loaded external catalogs before loading a new one (helps avoid duplicate label matches across versions when domain reload is disabled).")]
        private bool unloadPreviousExternalCatalogsOnStart = true;

        private static readonly System.Collections.Generic.List<AsyncOperationHandle<IResourceLocator>> s_externalCatalogHandles = new System.Collections.Generic.List<AsyncOperationHandle<IResourceLocator>>();
        private IResourceLocator _currentLocator;

        private void Start()
        {
            StartCoroutine(CoRun());
        }

        private IEnumerator CoRun()
        {
            var platformName = GetPlatformName();

            string resolvedCatalog = null;
            if (string.IsNullOrWhiteSpace(baseUrlRoot))
            {
                Debug.LogError("Base Url Root boş olamaz. Örnek: http://localhost:8000/ServerData");
                yield break;
            }
            var root = baseUrlRoot.TrimEnd('/');
            if (appendPlatformFolder) root += "/" + platformName;
            if (pickLatestVersion)
            {
                var chosen = default(string);
                yield return StartCoroutine(ChooseLatestVersionFolder(root, v => chosen = v));
                if (!string.IsNullOrEmpty(chosen)) root += "/" + chosen.Trim('/');
            }
            yield return StartCoroutine(TryResolveCatalogFromBase(root, url => resolvedCatalog = url));

            if (string.IsNullOrWhiteSpace(resolvedCatalog))
            {
                Debug.LogError("Catalog URL could not be resolved.");
                yield break;
            }

            if (unloadPreviousExternalCatalogsOnStart)
            {
                for (int i = s_externalCatalogHandles.Count - 1; i >= 0; i--)
                {
                    var h = s_externalCatalogHandles[i];
                    if (h.IsValid())
                    {
                        try
                        {
                            if (h.Result != null) Addressables.RemoveResourceLocator(h.Result);
                        }
                        catch { }
                        try { Addressables.Release(h); } catch { }
                    }
                    s_externalCatalogHandles.RemoveAt(i);
                }
            }

            var catHandle = default(AsyncOperationHandle<IResourceLocator>);
            bool catalogLoaded = false;
            for (int attempt = 0; attempt <= Mathf.Max(0, retryCount); attempt++)
            {
                catHandle = Addressables.LoadContentCatalogAsync(resolvedCatalog);
                yield return catHandle;
                if (catHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    catalogLoaded = true;
                    _currentLocator = catHandle.Result;
                    s_externalCatalogHandles.Add(catHandle);
                    break;
                }
                if (attempt < retryCount)
                {
                    var wait = retryDelaySeconds * Mathf.Pow(2f, attempt);
                    yield return new WaitForSeconds(wait);
                }
            }
            if (!catalogLoaded)
            {
                Debug.LogError("Catalog load failed after retries: " + catHandle.OperationException);
                yield break;
            }
            Debug.Log("Catalog loaded: " + resolvedCatalog);

            // Parent selection
            Transform instParent = parent;
            if (instParent == null && tryFindCanvasAsParent)
            {
#if UNITY_2023_1_OR_NEWER
                var canvas = FindFirstObjectByType<Canvas>();
#else
            var canvas = FindObjectOfType<Canvas>();
#endif
                if (canvas != null) instParent = canvas.transform;
            }

            // Load by label (use only the current external locator to avoid duplicates)
            if (!string.IsNullOrWhiteSpace(labelToLoad))
            {
                var labelKey = labelToLoad;
                if (intersectWithPlatformLabel)
                {
                    IList<IResourceLocation> a = null, b = null;
                    bool okA = _currentLocator != null && _currentLocator.Locate(labelKey, typeof(object), out a) && a != null;
                    bool okB = _currentLocator != null && _currentLocator.Locate(platformName, typeof(object), out b) && b != null;
                    if (okA && okB)
                    {
                        var setB = new HashSet<string>(b.Count);
                        foreach (var x in b) setB.Add(x.PrimaryKey);
                        foreach (var loc in a)
                        {
                            if (!setB.Contains(loc.PrimaryKey)) continue;
                            if (instantiate)
                            {
                                var instH = Addressables.InstantiateAsync(loc, instParent);
                                yield return instH;
                                if (instH.Status != AsyncOperationStatus.Succeeded)
                                    Debug.LogError($"Instantiate failed: {loc.PrimaryKey} => {instH.OperationException}");
                            }
                            else
                            {
                                var loadH = Addressables.LoadAssetAsync<Object>(loc);
                                yield return loadH;
                                if (loadH.Status != AsyncOperationStatus.Succeeded)
                                    Debug.LogError($"Load failed: {loc.PrimaryKey} => {loadH.OperationException}");
                            }
                        }
                        yield break;
                    }
                    // Fallback to global intersection if locator intersection failed
                    var keys = new List<string> { labelToLoad, platformName };
                    var locsIntersectH = Addressables.LoadResourceLocationsAsync(keys, Addressables.MergeMode.Intersection);
                    yield return locsIntersectH;
                    if (locsIntersectH.Status != AsyncOperationStatus.Succeeded)
                    {
                        Debug.LogError("Label locations failed: " + locsIntersectH.OperationException);
                        yield break;
                    }
                    foreach (var loc in locsIntersectH.Result)
                    {
                        if (instantiate)
                        {
                            var instH = Addressables.InstantiateAsync(loc, instParent);
                            yield return instH;
                            if (instH.Status != AsyncOperationStatus.Succeeded)
                                Debug.LogError($"Instantiate failed: {loc.PrimaryKey} => {instH.OperationException}");
                        }
                        else
                        {
                            var loadH = Addressables.LoadAssetAsync<Object>(loc);
                            yield return loadH;
                            if (loadH.Status != AsyncOperationStatus.Succeeded)
                                Debug.LogError($"Load failed: {loc.PrimaryKey} => {loadH.OperationException}");
                        }
                    }
                    yield break;
                }

                IList<IResourceLocation> locs = null;
                bool ok = _currentLocator != null && _currentLocator.Locate(labelKey, typeof(object), out locs) && locs != null && locs.Count > 0;
                if (!ok)
                {
                    // Fallback to global lookup if locator lookup fails
                    var locsH = Addressables.LoadResourceLocationsAsync(labelKey);
                    yield return locsH;
                    if (locsH.Status != AsyncOperationStatus.Succeeded)
                    {
                        Debug.LogError("Label locations failed: " + locsH.OperationException);
                        yield break;
                    }
                    foreach (var loc in locsH.Result)
                    {
                        if (instantiate)
                        {
                            var instH = Addressables.InstantiateAsync(loc, instParent);
                            yield return instH;
                            if (instH.Status != AsyncOperationStatus.Succeeded)
                                Debug.LogError($"Instantiate failed: {loc.PrimaryKey} => {instH.OperationException}");
                        }
                        else
                        {
                            var loadH = Addressables.LoadAssetAsync<Object>(loc);
                            yield return loadH;
                            if (loadH.Status != AsyncOperationStatus.Succeeded)
                                Debug.LogError($"Load failed: {loc.PrimaryKey} => {loadH.OperationException}");
                        }
                    }
                    yield break;
                }

                foreach (var loc in locs)
                {
                    if (instantiate)
                    {
                        var instH = Addressables.InstantiateAsync(loc, instParent);
                        yield return instH;
                        if (instH.Status != AsyncOperationStatus.Succeeded)
                            Debug.LogError($"Instantiate failed: {loc.PrimaryKey} => {instH.OperationException}");
                    }
                    else
                    {
                        var loadH = Addressables.LoadAssetAsync<Object>(loc);
                        yield return loadH;
                        if (loadH.Status != AsyncOperationStatus.Succeeded)
                            Debug.LogError($"Load failed: {loc.PrimaryKey} => {loadH.OperationException}");
                    }
                }
            }
            else
            {
                Debug.Log("Nothing to load: label is empty.");
            }
        }

        private IEnumerator ChooseVersionFolder(string platformRoot, string pinned, string min, System.Action<string> onChosen)
        {
            string result = null;
            using (var req = UnityWebRequest.Get(platformRoot.TrimEnd('/') + "/"))
            {
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok) { onChosen?.Invoke(null); yield break; }
                var html = req.downloadHandler.text ?? string.Empty;
                var folders = ExtractFolderLinks(html);
                if (folders.Count == 0) { onChosen?.Invoke(null); yield break; }
                if (!string.IsNullOrWhiteSpace(pinned))
                {
                    // Exact match first
                    result = folders.Find(f => f.Trim('/').Equals(pinned.Trim('/')));
                    onChosen?.Invoke(result);
                    yield break;
                }
                if (!string.IsNullOrWhiteSpace(min))
                {
                    // Try numeric compare with common prefix (v)
                    string best = null; int bestNum = int.MinValue;
                    foreach (var f in folders)
                    {
                        var name = f.Trim('/');
                        if (TryParseVersionNumber(name, out var n))
                        {
                            if (TryParseVersionNumber(min.Trim('/'), out var minNum))
                            {
                                if (n >= minNum && n > bestNum) { bestNum = n; best = name; }
                            }
                        }
                    }
                    if (best != null) { onChosen?.Invoke(best); yield break; }
                    // No fallback when min specified and none matched; return null
                    onChosen?.Invoke(null);
                    yield break;
                }
            }
            onChosen?.Invoke(result);
        }

        private IEnumerator ChooseLatestVersionFolder(string platformRoot, System.Action<string> onChosen)
        {
            using (var req = UnityWebRequest.Get(platformRoot.TrimEnd('/') + "/"))
            {
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok) { onChosen?.Invoke(null); yield break; }
                var html = req.downloadHandler.text ?? string.Empty;
                var folders = ExtractFolderLinks(html);
                if (folders.Count == 0) { onChosen?.Invoke(null); yield break; }
                // Prefer numeric max (with optional prefix like v); then lexicographic max
                string best = null; int bestNum = int.MinValue; bool foundNumeric = false;
                foreach (var f in folders)
                {
                    var name = f.Trim('/');
                    if (TryParseVersionNumber(name, out var n))
                    {
                        foundNumeric = true;
                        if (n > bestNum) { bestNum = n; best = name; }
                    }
                }
                if (!foundNumeric)
                {
                    foreach (var f in folders)
                    {
                        var name = f.Trim('/');
                        if (best == null || string.Compare(name, best, System.StringComparison.Ordinal) > 0) best = name;
                    }
                }
                onChosen?.Invoke(best);
            }
        }

        private static List<string> ExtractFolderLinks(string html)
        {
            var list = new List<string>();
            int idx = 0;
            while (idx < html.Length)
            {
                var hrefIdx = html.IndexOf("href=\"", idx, System.StringComparison.OrdinalIgnoreCase);
                if (hrefIdx < 0) break;
                hrefIdx += 6;
                var endIdx = html.IndexOf('"', hrefIdx);
                if (endIdx < 0) break;
                var link = html.Substring(hrefIdx, endIdx - hrefIdx);
                if (link.EndsWith("/")) list.Add(link);
                idx = endIdx + 1;
            }
            return list;
        }

        private static bool TryParseVersionNumber(string name, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(name)) return false;
            // Strip common prefix letters e.g., v001 -> 001
            int i = 0; while (i < name.Length && !char.IsDigit(name[i])) i++;
            var digits = name.Substring(i);
            return int.TryParse(digits, out number);
        }

        private IEnumerator TryResolveCatalogFromBase(string baseUrl, System.Action<string> onResolved)
        {
            string root = baseUrl.TrimEnd('/');
            // Try marker
            using (var req = UnityWebRequest.Get(root + "/latest_catalog.txt"))
            {
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (ok)
                {
                    var name = (req.downloadHandler.text ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        onResolved?.Invoke(root + "/" + name);
                        yield break;
                    }
                }
            }
            // Fallback scan
            using (var listReq = UnityWebRequest.Get(root + "/"))
            {
                yield return listReq.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool ok = listReq.result == UnityWebRequest.Result.Success;
#else
            bool ok = !listReq.isNetworkError && !listReq.isHttpError;
#endif
                if (ok)
                {
                    var html = listReq.downloadHandler.text ?? string.Empty;
                    var cands = ExtractCatalogJsonLinks(html);
                    if (cands.Count > 0)
                    {
                        onResolved?.Invoke(root + "/" + cands[0]);
                        yield break;
                    }
                }
            }
            onResolved?.Invoke(null);
        }

        private static List<string> ExtractCatalogJsonLinks(string html)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(html)) return list;
            int idx = 0;
            while (idx < html.Length)
            {
                var hrefIdx = html.IndexOf("href=\"", idx, System.StringComparison.OrdinalIgnoreCase);
                if (hrefIdx < 0) break;
                hrefIdx += 6;
                var endIdx = html.IndexOf('"', hrefIdx);
                if (endIdx < 0) break;
                var link = html.Substring(hrefIdx, endIdx - hrefIdx);
                if (link.IndexOf("catalog_", System.StringComparison.OrdinalIgnoreCase) >= 0 && link.EndsWith(".json"))
                {
                    if (link.StartsWith("./")) link = link.Substring(2);
                    list.Add(link);
                }
                idx = endIdx + 1;
            }
            return list;
        }

        private static string GetPlatformName()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#else
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsPlayer: return "StandaloneWindows64"; // adjust if 32-bit
            case RuntimePlatform.OSXPlayer: return "StandaloneOSX";
            case RuntimePlatform.LinuxPlayer: return "StandaloneLinux64";
            case RuntimePlatform.Android: return "Android";
            case RuntimePlatform.IPhonePlayer: return "iOS";
            default: return Application.platform.ToString();
        }
#endif
        }

        // ExtractVersionFromUrl no longer needed in simplified flow
    }
}
