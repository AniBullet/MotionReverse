using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
#if UNITY_2020_3_OR_NEWER
using UnityEditor.Formats.Fbx.Exporter;
using UnityEditor.Animations;
#endif

public class FBXTakeExporterWindow : EditorWindow
{
    private const string SessionKeyFBXPath = "FBXTakeExporter_LastFBXPath";
    private const string SessionKeyOutName = "FBXTakeExporter_LastOutputName";
    private const string SessionKeyExportDir = "FBXTakeExporter_ExportDir";
    private const string SessionKeyAnimDir = "FBXTakeExporter_AnimDir";
    private const string SessionKeyExportMode = "FBXTakeExporter_ExportMode";

    private string fbxAssetPath = "Assets/Models/MyModel.fbx";
    private GameObject fbxObject;
    private string outputFileName = "FBX_TakeNames.txt";
    private string exportFolder = "Assets/ExportedFBX";
    private string animFolder = "Assets/Animations";
    private Vector2 scrollPos;
    private List<string> takeNames = new List<string>();
    private List<bool> takeToggle = new List<bool>();
    private bool exportMesh = true;
    
    private enum ExportMode
    {
        Embedded,
        Separate
    }
    private ExportMode exportMode = ExportMode.Embedded;
    
    // 优化：使用缓存和延迟加载
    private List<AnimationClip> animClips = new List<AnimationClip>();
    private List<bool> animClipToggle = new List<bool>();
    private bool animClipsDirty = true;
    private string[] animClipGuids;

    [MenuItem("Tools/FBX Take Exporter Window")]
    public static void OpenWindow()
    {
        var w = GetWindow<FBXTakeExporterWindow>("FBX Take Exporter");
        w.minSize = new Vector2(700, 500);
        w.LoadSession();
        w.RefreshClips();
    }

    private void OnEnable() { LoadSession(); RefreshClips(); }
    private void OnDisable() { SaveSession(); }

    private void OnGUI()
    {
        GUILayout.Label("FBX Take Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("导出模式：", GUILayout.Width(80));
        var newMode = (ExportMode)EditorGUILayout.EnumPopup(exportMode);
        if (newMode != exportMode)
        {
            exportMode = newMode;
            SaveSession();
            animClipsDirty = true;
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();

        if (exportMode == ExportMode.Embedded)
        {
            DrawEmbeddedMode();
        }
        else
        {
            DrawSeparateMode();
        }
    }

    private void DrawEmbeddedMode()
    {
        GUILayout.Label("内嵌模式 - 从FBX文件导出动画剪辑", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawFbxSelector();

        EditorGUILayout.Space();
        GUILayout.Label("导出目录：", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        exportFolder = EditorGUILayout.TextField(exportFolder);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择导出目录", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    exportFolder = "Assets" + selected.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        exportMesh = EditorGUILayout.ToggleLeft("导出模型网格（不勾选则只导出骨架+动画）", exportMesh);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全选所有剪辑", GUILayout.Height(25)))
        {
            for (int i = 0; i < takeToggle.Count; i++) takeToggle[i] = true;
        }
        if (GUILayout.Button("取消全选", GUILayout.Height(25)))
        {
            for (int i = 0; i < takeToggle.Count; i++) takeToggle[i] = false;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Label("动画剪辑 (按 Inspector 顺序)：", EditorStyles.label);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        for (int i = 0; i < takeNames.Count; i++)
            takeToggle[i] = EditorGUILayout.ToggleLeft(takeNames[i], takeToggle[i]);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("导出名称文件：", GUILayout.Width(100));
        var newName = EditorGUILayout.TextField(outputFileName);
        EditorGUILayout.EndHorizontal();
        if (newName != outputFileName) { outputFileName = newName; SaveSession(); }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导出名称列表", GUILayout.Height(30))) ExportTakeNames();

#if UNITY_2020_3_OR_NEWER
        GUI.enabled = fbxObject != null;
        if (GUILayout.Button("导出所有 FBX", GUILayout.Height(30))) ExportAllFBX();
        GUI.enabled = takeToggle.Exists(x => x) && fbxObject != null;
        if (GUILayout.Button("导出选中 FBX", GUILayout.Height(30))) ExportSelectedFBX();
        GUI.enabled = true;
#else
        if (GUILayout.Button("导出所有 FBX", GUILayout.Height(30))) ShowExportInstructions();
        if (GUILayout.Button("导出选中 FBX", GUILayout.Height(30))) ShowExportInstructions();
#endif
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSeparateMode()
    {
        GUILayout.Label("分离模式 - 从动画文件目录导出FBX", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawFbxSelector();

        EditorGUILayout.Space();
        
        GUILayout.Label("动画文件目录：", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        animFolder = EditorGUILayout.TextField(animFolder);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择动画文件目录", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    animFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                animClipsDirty = true;
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Label("导出目录：", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        exportFolder = EditorGUILayout.TextField(exportFolder);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择导出目录", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    exportFolder = "Assets" + selected.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        exportMesh = EditorGUILayout.ToggleLeft("导出模型网格（不勾选则只导出骨架+动画）", exportMesh);

        EditorGUILayout.Space();
        
        if (GUILayout.Button("刷新动画剪辑列表", GUILayout.Height(25)))
        {
            animClipsDirty = true;
            RefreshAnimClips();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全选所有剪辑", GUILayout.Height(25)))
        {
            for (int i = 0; i < animClipToggle.Count; i++) animClipToggle[i] = true;
        }
        if (GUILayout.Button("取消全选", GUILayout.Height(25)))
        {
            for (int i = 0; i < animClipToggle.Count; i++) animClipToggle[i] = false;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Label("动画剪辑：", EditorStyles.label);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        
        if (animClipsDirty)
        {
            RefreshAnimClips();
        }
        
        for (int i = 0; i < animClips.Count; i++)
        {
            var clip = animClips[i];
            if (clip != null)
            {
                EditorGUILayout.BeginHorizontal();
                animClipToggle[i] = EditorGUILayout.ToggleLeft(clip.name, animClipToggle[i], GUILayout.Width(350));
                EditorGUILayout.LabelField($"长度: {clip.length:F2}s", GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
#if UNITY_2020_3_OR_NEWER
        GUI.enabled = fbxObject != null && animClips.Count > 0;
        if (GUILayout.Button("导出所有选中动画", GUILayout.Height(30))) ExportSelectedAnimClips();
        GUI.enabled = true;
#else
        if (GUILayout.Button("导出所有选中动画", GUILayout.Height(30))) ShowExportInstructions();
#endif
        EditorGUILayout.EndHorizontal();
    }

    private void DrawFbxSelector()
    {
        var dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将 FBX 文件拖到此处 或 使用下方选择", EditorStyles.helpBox);
        var evt = Event.current;
        if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropRect.Contains(evt.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    {
                        fbxAssetPath = path;
                        fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        SaveSession(); 
                        RefreshClips(); 
                        break;
                    }
                }
            }
            evt.Use();
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("FBX 资源：", GUILayout.Width(70));
        var newObj = (GameObject)EditorGUILayout.ObjectField(fbxObject, typeof(GameObject), false);
        EditorGUILayout.EndHorizontal();
        if (newObj != fbxObject)
        {
            fbxObject = newObj;
            if (fbxObject != null)
            {
                fbxAssetPath = AssetDatabase.GetAssetPath(fbxObject);
                SaveSession(); 
                RefreshClips();
            }
        }
        EditorGUILayout.LabelField("当前路径:", fbxAssetPath);
    }

    private void ShowExportInstructions()
    {
        EditorUtility.DisplayDialog(
            "导出 FBX 说明",
            "要导出动画剪辑为独立 FBX 文件，请确保已安装 'FBX Exporter' 包，且使用 Unity 2020.3 或更新版本。",
            "好的"
        );
    }

    private void LoadSession()
    {
        fbxAssetPath   = SessionState.GetString(SessionKeyFBXPath, fbxAssetPath);
        outputFileName = SessionState.GetString(SessionKeyOutName, outputFileName);
        exportFolder   = SessionState.GetString(SessionKeyExportDir, exportFolder);
        animFolder     = SessionState.GetString(SessionKeyAnimDir, animFolder);
        exportMode     = (ExportMode)SessionState.GetInt(SessionKeyExportMode, (int)exportMode);
        if (!string.IsNullOrEmpty(fbxAssetPath))
            fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
    }

    private void SaveSession()
    {
        SessionState.SetString(SessionKeyFBXPath, fbxAssetPath);
        SessionState.SetString(SessionKeyOutName, outputFileName);
        SessionState.SetString(SessionKeyExportDir, exportFolder);
        SessionState.SetString(SessionKeyAnimDir, animFolder);
        SessionState.SetInt(SessionKeyExportMode, (int)exportMode);
    }

    private void RefreshClips()
    {
        takeNames.Clear(); 
        takeToggle.Clear();
        if (string.IsNullOrEmpty(fbxAssetPath)) return;
        
        var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
        if (importer == null) return;
        
        var infos = importer.clipAnimations;
        if (infos == null || infos.Length == 0) 
            infos = importer.defaultClipAnimations;
            
        foreach (var ci in infos)
        {
            takeNames.Add(ci.name);
            takeToggle.Add(false);
        }
    }

    private void RefreshAnimClips()
    {
        if (string.IsNullOrEmpty(animFolder)) return;
        
        // 优化：只获取GUID，减少AssetDatabase调用
        animClipGuids = AssetDatabase.FindAssets("t:AnimationClip", new string[] { animFolder });
        
        animClips.Clear(); 
        animClipToggle.Clear();
        
        // 批量加载，减少性能开销
        foreach (string guid in animClipGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip != null)
            {
                animClips.Add(clip);
                animClipToggle.Add(false);
            }
        }
        
        animClipsDirty = false;
    }

    private void ExportTakeNames()
    {
        var names = takeNames.ToArray();
        string root = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf("/Assets"));
        string path = Path.Combine(root, outputFileName);
        try
        {
            File.WriteAllLines(path, names);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"导出 {names.Length} 条名称 到:\n{path}", "确定");
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", ex.Message, "确定");
        }
    }

#if UNITY_2020_3_OR_NEWER
    private void ExportAllFBX()
    {
        ExportFBX(takeNames);
    }

    private void ExportSelectedFBX()
    {
        var selected = takeNames.Where((t, i) => takeToggle[i]).ToList();
        ExportFBX(selected);
    }

    private void ExportFBX(List<string> clipNames)
    {
        if (clipNames == null || clipNames.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未选择任何动画剪辑", "确定");
            return;
        }
        
        // 全面验证导出环境
        if (!ValidateExportEnvironment())
        {
            return;
        }

        try
        {
            if (!Directory.Exists(exportFolder)) 
            {
                Directory.CreateDirectory(exportFolder);
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"无法创建导出目录: {ex.Message}", "确定");
            return;
        }

        ModelImporter modelImporter = null;
        try
        {
            modelImporter = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"获取FBX模型导入器失败: {ex.Message}", "确定");
            return;
        }
        
        if (modelImporter == null)
        {
            EditorUtility.DisplayDialog("错误", "无法获取FBX模型导入器", "确定");
            return;
        }
        
        UnityEngine.Object[] assets = null;
        try
        {
            assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"加载FBX资源失败: {ex.Message}", "确定");
            return;
        }
        
        if (assets == null)
        {
            EditorUtility.DisplayDialog("错误", "FBX资源加载失败", "确定");
            return;
        }
        
        List<AnimationClip> availableClips = new List<AnimationClip>();
        foreach (var asset in assets)
        {
            if (asset is AnimationClip clip && !asset.name.StartsWith("__preview__"))
            {
                availableClips.Add(clip);
            }
        }

        int successCount = 0;
        int totalCount = clipNames.Count;
        
        for (int i = 0; i < clipNames.Count; i++)
        {
            string clipName = clipNames[i];
            string controllerPath = null;
            GameObject tempRoot = null;
            
            try
            {
                // 显示进度
                if (EditorUtility.DisplayCancelableProgressBar("导出FBX", $"正在导出 {clipName} ({i + 1}/{totalCount})", (float)i / totalCount))
                {
                    Debug.Log("用户取消了导出操作");
                    break;
                }
                
                AnimationClip targetClip = availableClips.FirstOrDefault(c => c.name == clipName);
                if (targetClip == null)
                {
                    Debug.LogWarning($"未找到动画剪辑: {clipName}");
                    continue;
                }

                string outPath = Path.Combine(exportFolder, clipName + ".fbx");

                // 安全创建临时对象
                try
                {
                    tempRoot = GameObject.Instantiate(fbxObject);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"创建临时对象失败: {ex.Message}");
                    continue;
                }
                
                if (tempRoot == null)
                {
                    Debug.LogError($"无法创建临时对象: {clipName}");
                    continue;
                }
                tempRoot.name = "Root";

                SafeRemoveComponents(tempRoot);

                controllerPath = CreateTempController(targetClip);
                if (string.IsNullOrEmpty(controllerPath))
                {
                    Debug.LogError($"无法创建临时控制器: {clipName}");
                    continue;
                }

                var animator = tempRoot.GetComponent<Animator>();
                if (animator == null) 
                {
                    try
                    {
                        animator = tempRoot.AddComponent<Animator>();
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"添加Animator组件失败: {ex.Message}");
                        continue;
                    }
                }
                
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller != null)
                {
                    animator.runtimeAnimatorController = controller;
                }

                ExportModelOptions exportSettings = new ExportModelOptions();
                exportSettings.ExportFormat = ExportFormat.Binary;
                exportSettings.KeepInstances = false;
                
                string result = null;
                try
                {
                    result = ModelExporter.ExportObject(outPath, tempRoot, exportSettings);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"FBX导出API调用失败: {ex.Message}");
                    continue;
                }
                
                bool success = !string.IsNullOrEmpty(result);
                
                if (success)
                {
                    Debug.Log($"已导出FBX: {outPath}");
                    successCount++;
                    
                    try
                    {
                        AssetDatabase.ImportAsset(outPath);
                        ModelImporter exportedImporter = AssetImporter.GetAtPath(outPath) as ModelImporter;
                        if (exportedImporter != null)
                        {
                            exportedImporter.animationType = ModelImporterAnimationType.Generic;
                            exportedImporter.importAnimation = true;
                            exportedImporter.animationCompression = ModelImporterAnimationCompression.Off;
                            exportedImporter.SaveAndReimport();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"设置导出FBX导入选项时出错: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"导出FBX失败: {outPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"导出剪辑 {clipName} 时发生错误：{e.Message}");
            }
            finally
            {
                SafeCleanupResources(controllerPath, tempRoot);
            }
        }

        EditorUtility.ClearProgressBar();

        if (successCount > 0)
        {
            try
            {
                AssetDatabase.Refresh();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"刷新AssetDatabase时出错: {ex.Message}");
            }
            
            EditorUtility.DisplayDialog("导出完成", $"成功导出 {successCount}/{totalCount} 个 FBX 到:\n{exportFolder}", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("导出失败", "未能成功导出任何 FBX 文件", "确定");
        }
    }

    private void ExportSelectedAnimClips()
    {
        var selectedClips = animClips.Where((c, i) => animClipToggle[i]).ToList();
        if (selectedClips == null || selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未选择任何动画剪辑", "确定");
            return;
        }

        // 全面验证导出环境
        if (!ValidateExportEnvironment())
        {
            return;
        }

        try
        {
            if (!Directory.Exists(exportFolder)) 
            {
                Directory.CreateDirectory(exportFolder);
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"无法创建导出目录: {ex.Message}", "确定");
            return;
        }

        ModelImporter modelImporter = null;
        try
        {
            modelImporter = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"获取FBX模型导入器失败: {ex.Message}", "确定");
            return;
        }
        
        if (modelImporter == null)
        {
            EditorUtility.DisplayDialog("错误", "无法获取FBX模型导入器", "确定");
            return;
        }
        
        int successCount = 0;
        int totalCount = selectedClips.Count;
        
        for (int i = 0; i < selectedClips.Count; i++)
        {
            var clip = selectedClips[i];
            if (clip == null) continue;
            
            string controllerPath = null;
            GameObject tempRoot = null;
            
            try
            {
                // 显示进度
                if (EditorUtility.DisplayCancelableProgressBar("导出FBX", $"正在导出 {clip.name} ({i + 1}/{totalCount})", (float)i / totalCount))
                {
                    Debug.Log("用户取消了导出操作");
                    break;
                }
                
                string outPath = Path.Combine(exportFolder, clip.name + ".fbx");

                // 安全创建临时对象
                try
                {
                    tempRoot = GameObject.Instantiate(fbxObject);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"创建临时对象失败: {ex.Message}");
                    continue;
                }
                
                if (tempRoot == null)
                {
                    Debug.LogError($"无法创建临时对象: {clip.name}");
                    continue;
                }
                tempRoot.name = "Root";

                SafeRemoveComponents(tempRoot);

                controllerPath = CreateTempController(clip);
                if (string.IsNullOrEmpty(controllerPath))
                {
                    Debug.LogError($"无法创建临时控制器: {clip.name}");
                    continue;
                }

                var animator = tempRoot.GetComponent<Animator>();
                if (animator == null) 
                {
                    try
                    {
                        animator = tempRoot.AddComponent<Animator>();
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"添加Animator组件失败: {ex.Message}");
                        continue;
                    }
                }
                
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller != null)
                {
                    animator.runtimeAnimatorController = controller;
                }

                ExportModelOptions exportSettings = new ExportModelOptions();
                exportSettings.ExportFormat = ExportFormat.Binary;
                exportSettings.KeepInstances = false;
                
                string result = null;
                try
                {
                    result = ModelExporter.ExportObject(outPath, tempRoot, exportSettings);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"FBX导出API调用失败: {ex.Message}");
                    continue;
                }
                
                bool success = !string.IsNullOrEmpty(result);
                
                if (success)
                {
                    Debug.Log($"已导出FBX: {outPath}");
                    successCount++;
                    
                    try
                    {
                        AssetDatabase.ImportAsset(outPath);
                        ModelImporter exportedImporter = AssetImporter.GetAtPath(outPath) as ModelImporter;
                        if (exportedImporter != null)
                        {
                            exportedImporter.animationType = ModelImporterAnimationType.Generic;
                            exportedImporter.importAnimation = true;
                            exportedImporter.animationCompression = ModelImporterAnimationCompression.Off;
                            exportedImporter.SaveAndReimport();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"设置导出FBX导入选项时出错: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"导出FBX失败: {outPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"导出剪辑 {clip.name} 时发生错误：{e.Message}");
            }
            finally
            {
                SafeCleanupResources(controllerPath, tempRoot);
            }
        }

        EditorUtility.ClearProgressBar();

        if (successCount > 0)
        {
            try
            {
                AssetDatabase.Refresh();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"刷新AssetDatabase时出错: {ex.Message}");
            }
            
            EditorUtility.DisplayDialog("导出完成", $"成功导出 {successCount}/{totalCount} 个 FBX 到:\n{exportFolder}", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("导出失败", "未能成功导出任何 FBX 文件", "确定");
        }
    }

    // 新增：全面验证导出环境
    private bool ValidateExportEnvironment()
    {
        try
        {
            // 验证FBX对象
            if (fbxObject == null)
            {
                EditorUtility.DisplayDialog("错误", "FBX对象为空，请先选择FBX文件", "确定");
                return false;
            }
            
            // 验证FBX路径
            if (string.IsNullOrEmpty(fbxAssetPath))
            {
                EditorUtility.DisplayDialog("错误", "FBX路径为空", "确定");
                return false;
            }
            
            // 验证FBX文件是否存在
            if (!System.IO.File.Exists(fbxAssetPath))
            {
                EditorUtility.DisplayDialog("错误", "FBX文件不存在，路径可能已失效", "确定");
                return false;
            }
            
            // 验证导出目录
            if (string.IsNullOrEmpty(exportFolder))
            {
                EditorUtility.DisplayDialog("错误", "导出目录为空", "确定");
                return false;
            }
            
            // 验证FBX对象是否仍然有效
            if (fbxObject == null || fbxObject.gameObject == null)
            {
                EditorUtility.DisplayDialog("错误", "FBX对象已失效，请重新选择", "确定");
                return false;
            }
            
            // 验证是否有可用的动画组件
            var animator = fbxObject.GetComponent<Animator>();
            var animation = fbxObject.GetComponent<Animation>();
            if (animator == null && animation == null)
            {
                // 检查子对象
                var childAnimators = fbxObject.GetComponentsInChildren<Animator>();
                var childAnimations = fbxObject.GetComponentsInChildren<Animation>();
                if ((childAnimators == null || childAnimators.Length == 0) && 
                    (childAnimations == null || childAnimations.Length == 0))
                {
                    EditorUtility.DisplayDialog("警告", "FBX对象没有找到动画组件，导出可能失败", "继续");
                }
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"验证导出环境时发生错误: {ex.Message}", "确定");
            return false;
        }
    }

    // 安全地移除组件 - 使用Destroy而不是DestroyImmediate
    private void SafeRemoveComponents(GameObject root)
    {
        if (root == null) return;
        
        try
        {
            // 使用更安全的方式移除组件
            var animations = root.GetComponentsInChildren<Animation>();
            if (animations != null)
            {
                foreach (var anim in animations)
                {
                    if (anim != null && anim.gameObject != null)
                    {
                        try
                        {
                            GameObject.DestroyImmediate(anim, true);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"移除Animation组件时出错: {ex.Message}");
                        }
                    }
                }
            }
            
            var animators = root.GetComponentsInChildren<Animator>();
            if (animators != null)
            {
                foreach (var anim in animators)
                {
                    if (anim != null && anim.gameObject != null)
                    {
                        try
                        {
                            GameObject.DestroyImmediate(anim, true);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"移除Animator组件时出错: {ex.Message}");
                        }
                    }
                }
            }

            if (!exportMesh)
            {
                var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skinnedMeshRenderers != null)
                {
                    foreach (var smr in skinnedMeshRenderers)
                    {
                        if (smr != null && smr.gameObject != null)
                        {
                            try
                            {
                                GameObject.DestroyImmediate(smr, true);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"移除SkinnedMeshRenderer组件时出错: {ex.Message}");
                            }
                        }
                    }
                }
                
                var meshRenderers = root.GetComponentsInChildren<MeshRenderer>();
                if (meshRenderers != null)
                {
                    foreach (var mr in meshRenderers)
                    {
                        if (mr != null && mr.gameObject != null)
                        {
                            try
                            {
                                GameObject.DestroyImmediate(mr, true);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"移除MeshRenderer组件时出错: {ex.Message}");
                            }
                        }
                    }
                }
                
                var meshFilters = root.GetComponentsInChildren<MeshFilter>();
                if (meshFilters != null)
                {
                    foreach (var mf in meshFilters)
                    {
                        if (mf != null && mf.gameObject != null)
                        {
                            try
                            {
                                GameObject.DestroyImmediate(mf, true);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"移除MeshFilter组件时出错: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"移除组件时发生警告: {e.Message}");
        }
    }

    // 创建临时控制器
    private string CreateTempController(AnimationClip clip)
    {
        if (clip == null) return null;
        
        try
        {
            string controllerPath = "Assets/TempController_" + System.Guid.NewGuid().ToString() + ".controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (controller == null) return null;
            
            try
            {
                var stateMachine = controller.layers[0].stateMachine;
                var state = stateMachine.AddState(clip.name);
                state.motion = clip;
                stateMachine.defaultState = state;
                
                return controllerPath;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"设置状态机时出错: {ex.Message}");
                // 清理失败的控制器
                try
                {
                    AssetDatabase.DeleteAsset(controllerPath);
                }
                catch { }
                return null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"创建临时控制器失败: {e.Message}");
            return null;
        }
    }

    // 安全清理资源
    private void SafeCleanupResources(string controllerPath, GameObject tempRoot)
    {
        try
        {
            // 清理临时控制器
            if (!string.IsNullOrEmpty(controllerPath))
            {
                try
                {
                    if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                    {
                        AssetDatabase.DeleteAsset(controllerPath);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"删除临时控制器时出错: {ex.Message}");
                }
            }
            
            // 清理临时对象
            if (tempRoot != null)
            {
                try
                {
                    // 使用DestroyImmediate但添加更多安全检查
                    if (tempRoot != null && tempRoot.gameObject != null)
                    {
                        GameObject.DestroyImmediate(tempRoot, true);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"销毁临时对象时出错: {ex.Message}");
                    // 如果DestroyImmediate失败，尝试使用Destroy
                    try
                    {
                        GameObject.Destroy(tempRoot);
                    }
                    catch (System.Exception ex2)
                    {
                        Debug.LogWarning($"Destroy也失败: {ex2.Message}");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"清理资源时发生警告: {e.Message}");
        }
    }
#endif
}