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
    private const string SessionKeyClipMode = "FBXTakeExporter_ClipMode";
    private const string SessionKeyExternalClipsPath = "FBXTakeExporter_ExternalClipsPath";

    private string     fbxAssetPath   = "Assets/Models/MyModel.fbx";
    private GameObject fbxObject;
    private string     outputFileName = "FBX_TakeNames.txt";
    private string     exportFolder   = "Assets/ExportedFBX";
    private Vector2    scrollPos;
    private List<string> takeNames = new List<string>();
    private List<bool>   takeToggle = new List<bool>();
    private bool exportMesh = true; // 新增：是否导出模型网格
    
    // 新增：外部动画剪辑支持
    private enum ClipSourceMode
    {
        FBXInternal,    // FBX内部剪辑
        AnimClip,  // 外部动画文件
        AnimClipFolder  // 外部文件夹
    }
    private ClipSourceMode clipMode = ClipSourceMode.FBXInternal;
    private string externalClipsPath = "Assets/Animations";
    private List<AnimationClip> externalClips = new List<AnimationClip>();
    
    // 新增：外部模式下的模型选择
    private GameObject externalModelObject;
    private string externalModelPath = "";

    [MenuItem("Tools/FBX Take Exporter Window")]
    public static void OpenWindow()
    {
        var w = GetWindow<FBXTakeExporterWindow>("FBX Take Exporter");
        w.minSize = new Vector2(520, 500);
        w.LoadSession();
        w.RefreshClips();
    }

    private void OnEnable()  { LoadSession(); RefreshClips(); }
    private void OnDisable() { SaveSession(); }

    private void OnGUI()
    {
        GUILayout.Label("FBX Take Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 新增：剪辑源模式选择
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("剪辑来源：", EditorStyles.label, GUILayout.Width(80));
        var newMode = (ClipSourceMode)EditorGUILayout.EnumPopup(clipMode, GUILayout.Width(120));
        if (newMode != clipMode)
        {
            clipMode = newMode;
            SaveSession();
            RefreshClips();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 根据模式显示不同的选择器
        switch (clipMode)
        {
            case ClipSourceMode.FBXInternal:
                DrawFbxSelector();
                break;
            case ClipSourceMode.AnimClip:
                DrawExternalFilesSelector();
                break;
            case ClipSourceMode.AnimClipFolder:
                DrawExternalFolderSelector();
                break;
        }

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

        // 新增：是否导出模型网格勾选项
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

        string clipLabel = clipMode == ClipSourceMode.FBXInternal ? "动画剪辑 (按 Inspector 顺序)：" : "外部动画剪辑：";
        GUILayout.Label(clipLabel, EditorStyles.label);
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
        GUI.enabled = (fbxObject != null || (clipMode != ClipSourceMode.FBXInternal && externalModelObject != null));
        if (GUILayout.Button("导出所有 FBX", GUILayout.Height(30))) ExportAllFBX();
        GUI.enabled = takeToggle.Exists(x => x) && (fbxObject != null || (clipMode != ClipSourceMode.FBXInternal && externalModelObject != null));
        if (GUILayout.Button("导出选中 FBX", GUILayout.Height(30))) ExportSelectedFBX();
        GUI.enabled = true;
#else
        if (GUILayout.Button("导出所有 FBX", GUILayout.Height(30))) ShowExportInstructions();
        if (GUILayout.Button("导出选中 FBX", GUILayout.Height(30))) ShowExportInstructions();
#endif
        EditorGUILayout.EndHorizontal();
    }

    // 新增：外部文件选择器
    private void DrawExternalFilesSelector()
    {
        // 添加模型选择
        EditorGUILayout.Space();
        GUILayout.Label("模型选择（用于承载动画）：", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("模型对象：", GUILayout.Width(70));
        var newModelObj = (GameObject)EditorGUILayout.ObjectField(externalModelObject, typeof(GameObject), false);
        EditorGUILayout.EndHorizontal();
        if (newModelObj != externalModelObject)
        {
            externalModelObject = newModelObj;
            if (externalModelObject != null)
            {
                externalModelPath = AssetDatabase.GetAssetPath(externalModelObject);
                SaveSession();
            }
        }
        if (externalModelObject != null)
        {
            EditorGUILayout.LabelField("模型路径:", externalModelPath);
        }
        else
        {
            EditorGUILayout.HelpBox("请选择一个模型对象来承载动画", MessageType.Warning);
        }

        EditorGUILayout.Space();
        var dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将动画文件(.anim)拖到此处 或 使用下方选择", EditorStyles.helpBox);
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
                    if (path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        if (clip != null && !externalClips.Contains(clip))
                        {
                            externalClips.Add(clip);
                            SaveSession();
                            RefreshClips();
                        }
                    }
                }
            }
            evt.Use();
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("动画文件：", GUILayout.Width(70));
        var newClip = (AnimationClip)EditorGUILayout.ObjectField(null, typeof(AnimationClip), false);
        EditorGUILayout.EndHorizontal();
        if (newClip != null && !externalClips.Contains(newClip))
        {
            externalClips.Add(newClip);
            SaveSession();
            RefreshClips();
        }

        // 显示已选择的文件
        if (externalClips.Count > 0)
        {
            EditorGUILayout.Space();
            GUILayout.Label("已选择的动画文件：", EditorStyles.label);
            for (int i = 0; i < externalClips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(externalClips[i], typeof(AnimationClip), false);
                if (GUILayout.Button("移除", GUILayout.Width(50)))
                {
                    externalClips.RemoveAt(i);
                    SaveSession();
                    RefreshClips();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    // 新增：外部文件夹选择器
    private void DrawExternalFolderSelector()
    {
        // 添加模型选择
        EditorGUILayout.Space();
        GUILayout.Label("模型选择（用于承载动画）：", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("模型对象：", GUILayout.Width(70));
        var newModelObj = (GameObject)EditorGUILayout.ObjectField(externalModelObject, typeof(GameObject), false);
        EditorGUILayout.EndHorizontal();
        if (newModelObj != externalModelObject)
        {
            externalModelObject = newModelObj;
            if (externalModelObject != null)
            {
                externalModelPath = AssetDatabase.GetAssetPath(externalModelObject);
                SaveSession();
            }
        }
        if (externalModelObject != null)
        {
            EditorGUILayout.LabelField("模型路径:", externalModelPath);
        }
        else
        {
            EditorGUILayout.HelpBox("请选择一个模型对象来承载动画", MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("动画文件夹：", GUILayout.Width(80));
        externalClipsPath = EditorGUILayout.TextField(externalClipsPath);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择动画文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                {
                    externalClipsPath = "Assets" + selected.Substring(Application.dataPath.Length);
                    SaveSession();
                    RefreshClips();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (Directory.Exists(Path.Combine(Application.dataPath, externalClipsPath.Replace("Assets/", ""))))
        {
            EditorGUILayout.LabelField("当前路径:", externalClipsPath);
        }
        else
        {
            EditorGUILayout.HelpBox("文件夹不存在，请选择有效的动画文件夹", MessageType.Warning);
        }
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
                        SaveSession(); RefreshClips(); break;
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
                SaveSession(); RefreshClips();
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
        clipMode       = (ClipSourceMode)SessionState.GetInt(SessionKeyClipMode, (int)clipMode);
        externalClipsPath = SessionState.GetString(SessionKeyExternalClipsPath, externalClipsPath);
        externalModelPath = SessionState.GetString("FBXTakeExporter_ExternalModelPath", externalModelPath);
        
        if (!string.IsNullOrEmpty(fbxAssetPath))
            fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
        if (!string.IsNullOrEmpty(externalModelPath))
            externalModelObject = AssetDatabase.LoadAssetAtPath<GameObject>(externalModelPath);
    }

    private void SaveSession()
    {
        SessionState.SetString(SessionKeyFBXPath, fbxAssetPath);
        SessionState.SetString(SessionKeyOutName, outputFileName);
        SessionState.SetString(SessionKeyExportDir, exportFolder);
        SessionState.SetInt(SessionKeyClipMode, (int)clipMode);
        SessionState.SetString(SessionKeyExternalClipsPath, externalClipsPath);
        SessionState.SetString("FBXTakeExporter_ExternalModelPath", externalModelPath);
    }

    private void RefreshClips()
    {
        takeNames.Clear(); 
        takeToggle.Clear();
        externalClips.Clear();

        switch (clipMode)
        {
            case ClipSourceMode.FBXInternal:
                RefreshFBXClips();
                break;
            case ClipSourceMode.AnimClip:
                RefreshExternalFilesClips();
                break;
            case ClipSourceMode.AnimClipFolder:
                RefreshExternalFolderClips();
                break;
        }
    }

    private void RefreshFBXClips()
    {
        if (string.IsNullOrEmpty(fbxAssetPath)) return;
        var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
        if (importer == null) return;
        var infos = importer.clipAnimations;
        if (infos == null || infos.Length == 0) infos = importer.defaultClipAnimations;
        foreach (var ci in infos)
        {
            takeNames.Add(ci.name);
            takeToggle.Add(false);
        }
    }

    private void RefreshExternalFilesClips()
    {
        foreach (var clip in externalClips)
        {
            if (clip != null)
            {
                takeNames.Add(clip.name);
                takeToggle.Add(false);
            }
        }
    }

    private void RefreshExternalFolderClips()
    {
        if (string.IsNullOrEmpty(externalClipsPath)) return;
        
        string fullPath = Path.Combine(Application.dataPath, externalClipsPath.Replace("Assets/", ""));
        if (!Directory.Exists(fullPath)) return;

        string[] animFiles = Directory.GetFiles(fullPath, "*.anim", SearchOption.AllDirectories);
        foreach (string filePath in animFiles)
        {
            string relativePath = "Assets" + filePath.Substring(Application.dataPath.Length);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(relativePath);
            if (clip != null)
            {
                takeNames.Add(clip.name);
                takeToggle.Add(false);
            }
        }
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
        if (clipNames.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未选择任何动画剪辑", "确定");
            return;
        }
        
        if (!Directory.Exists(exportFolder)) 
        {
            Directory.CreateDirectory(exportFolder);
        }

        // 获取动画剪辑列表
        List<AnimationClip> targetClips = new List<AnimationClip>();
        
        switch (clipMode)
        {
            case ClipSourceMode.FBXInternal:
                if (fbxObject == null)
                {
                    EditorUtility.DisplayDialog("错误", "请先选择FBX文件", "确定");
                    return;
                }
                // 获取FBX内部的动画剪辑
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !asset.name.StartsWith("__preview__"))
                    {
                        if (clipNames.Contains(clip.name))
                        {
                            targetClips.Add(clip);
                        }
                    }
                }
                break;
                
            case ClipSourceMode.AnimClip:
                // 检查是否选择了模型
                if (externalModelObject == null)
                {
                    EditorUtility.DisplayDialog("错误", "请先选择用于承载动画的模型", "确定");
                    return;
                }
                // 使用外部文件中的动画剪辑
                foreach (var clip in externalClips)
                {
                    if (clip != null && clipNames.Contains(clip.name))
                    {
                        targetClips.Add(clip);
                    }
                }
                break;
                
            case ClipSourceMode.AnimClipFolder:
                // 检查是否选择了模型
                if (externalModelObject == null)
                {
                    EditorUtility.DisplayDialog("错误", "请先选择用于承载动画的模型", "确定");
                    return;
                }
                // 从文件夹中获取动画剪辑
                string fullPath = Path.Combine(Application.dataPath, externalClipsPath.Replace("Assets/", ""));
                if (!Directory.Exists(fullPath))
                {
                    EditorUtility.DisplayDialog("错误", "动画文件夹不存在", "确定");
                    return;
                }
                
                string[] animFiles = Directory.GetFiles(fullPath, "*.anim", SearchOption.AllDirectories);
                foreach (string filePath in animFiles)
                {
                    string relativePath = "Assets" + filePath.Substring(Application.dataPath.Length);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(relativePath);
                    if (clip != null && clipNames.Contains(clip.name))
                    {
                        targetClips.Add(clip);
                    }
                }
                break;
        }

        if (targetClips.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未找到匹配的动画剪辑", "确定");
            return;
        }

        int successCount = 0;
        foreach (AnimationClip targetClip in targetClips)
        {
            string controllerPath = null;
            GameObject tempRoot = null;
            try
            {
                // 目标文件路径
                string outPath = Path.Combine(exportFolder, targetClip.name + ".fbx");

                // 创建临时对象（如果是外部剪辑，需要创建一个简单的骨架）
                if (clipMode == ClipSourceMode.FBXInternal)
                {
                    tempRoot = GameObject.Instantiate(fbxObject);
                    tempRoot.name = fbxObject.name;
                }
                else
                {
                    // 为外部剪辑使用选择的模型
                    tempRoot = GameObject.Instantiate(externalModelObject);
                    tempRoot.name = externalModelObject.name;
                }

                // 移除所有Animation/Animator组件，重新挂载Animator
                foreach (var anim in tempRoot.GetComponentsInChildren<Animation>())
                    GameObject.DestroyImmediate(anim);
                foreach (var anim in tempRoot.GetComponentsInChildren<Animator>())
                    GameObject.DestroyImmediate(anim);
                var animator = tempRoot.AddComponent<Animator>();

                // 移除网格组件（如果不导出网格）
                if (!exportMesh)
                {
                    foreach (var smr in tempRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                        GameObject.DestroyImmediate(smr);
                    foreach (var mr in tempRoot.GetComponentsInChildren<MeshRenderer>())
                        GameObject.DestroyImmediate(mr);
                    foreach (var mf in tempRoot.GetComponentsInChildren<MeshFilter>())
                        GameObject.DestroyImmediate(mf);
                }

                // 创建临时AnimatorController
                controllerPath = "Assets/TempController_" + System.Guid.NewGuid().ToString() + ".controller";
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var stateMachine = controller.layers[0].stateMachine;
                var state = stateMachine.AddState(targetClip.name);
                state.motion = targetClip;
                stateMachine.defaultState = state;
                animator.runtimeAnimatorController = controller;

                // 使用FBX Exporter API导出
                ExportModelOptions exportSettings = new ExportModelOptions();
                exportSettings.ExportFormat = ExportFormat.Binary;
                exportSettings.KeepInstances = false;
                
                string result = ModelExporter.ExportObject(outPath, tempRoot, exportSettings);
                bool success = !string.IsNullOrEmpty(result);
                
                if (success)
                {
                    Debug.Log($"已导出FBX: {outPath}");
                    successCount++;
                    
                    // 设置导入选项
                    AssetDatabase.ImportAsset(outPath);
                    ModelImporter exportedImporter = AssetImporter.GetAtPath(outPath) as ModelImporter;
                    if (exportedImporter != null)
                    {
                        // 基本设置
                        exportedImporter.animationType = ModelImporterAnimationType.Generic;
                        exportedImporter.importAnimation = true;
                        exportedImporter.animationCompression = ModelImporterAnimationCompression.Off;
                        
                        exportedImporter.SaveAndReimport();
                    }
                }
                else
                {
                    Debug.LogError($"导出FBX失败: {outPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"导出剪辑 {targetClip.name} 时发生错误：{e.Message}\n{e.StackTrace}");
            }
            finally
            {
                if (controllerPath != null && AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                {
                    AssetDatabase.DeleteAsset(controllerPath);
                }
                if (tempRoot != null)
                {
                    GameObject.DestroyImmediate(tempRoot);
                }
            }
        }

        if (successCount > 0)
        {
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("导出完成", $"成功导出 {successCount}/{targetClips.Count} 个 FBX 到:\n{exportFolder}", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("导出失败", "未能成功导出任何 FBX 文件", "确定");
        }
    }
#endif
}