using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Collections;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor.Animations;

public class AnimationModeConverterWindow : EditorWindow
{
    private const string SessionKeyFBXPath = "AnimationModeConverter_LastFBXPath";
    private const string SessionKeyAnimDir = "AnimationModeConverter_AnimDir";
    private const string SessionKeyExportDir = "AnimationModeConverter_ExportDir";
    private const string SessionKeyPositionError = "AnimationModeConverter_PositionError";
    private const string SessionKeyRotationError = "AnimationModeConverter_RotationError";
    private const string SessionKeyFrameRate = "AnimationModeConverter_FrameRate";

    private string fbxAssetPath = "Assets/Models/MyModel.fbx";
    private GameObject fbxObject;
    private string animFolder = "Assets/Animations";
    private string exportFolder = "Assets/ConvertedAnimations";
    private Vector2 scrollPos;
    
    // 动画剪辑列表
    private List<AnimationClip> animClips = new List<AnimationClip>();
    private List<bool> animClipToggle = new List<bool>();
    
    // 录制参数
    private float positionError = 0.05f;
    private float rotationError = 0.05f;
    private float frameRate = 60f;
    
    // 进度显示
    private bool isConverting = false;
    private float conversionProgress = 0f;
    private string currentStatus = "";

    [MenuItem("Tools/Animation Mode Converter")]
    public static void OpenWindow()
    {
        var w = GetWindow<AnimationModeConverterWindow>("Animation Mode Converter");
        w.minSize = new Vector2(1000, 600);
        w.position = new Rect(w.position.x, w.position.y, 1200, 700);
        w.LoadSession();
        w.RefreshAnimClips();
    }

    private void OnEnable() { LoadSession(); RefreshAnimClips(); }
    private void OnDisable() { SaveSession(); }

    private void OnGUI()
    {
        GUILayout.Label("Animation Mode Converter - H模式转G模式", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // FBX模型选择
        DrawFbxSelector();

        EditorGUILayout.Space();

        // 动画目录选择
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
                RefreshAnimClips();
            }
        }
        EditorGUILayout.EndHorizontal();

        // 导出目录选择
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

        EditorGUILayout.Space();

        // 录制参数设置
        GUILayout.Label("录制参数设置：", EditorStyles.boldLabel);
        positionError = EditorGUILayout.Slider("位置误差", positionError, 0.01f, 0.5f);
        rotationError = EditorGUILayout.Slider("旋转误差", rotationError, 0.01f, 0.5f);
        frameRate = EditorGUILayout.Slider("帧率", frameRate, 24f, 120f);
        
        EditorGUILayout.Space();

        // 刷新按钮
        if (GUILayout.Button("刷新动画剪辑列表", GUILayout.Height(25)))
        {
            RefreshAnimClips();
        }

        // 全选/取消全选按钮
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

        // 动画剪辑列表
        GUILayout.Label("动画剪辑：", EditorStyles.label);
        
        // 动画剪辑列表滚动视图
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
        
        for (int i = 0; i < animClips.Count; i++)
        {
            var clip = animClips[i];
            if (clip != null)
            {
                EditorGUILayout.BeginHorizontal();
                // 增加名称显示宽度
                animClipToggle[i] = EditorGUILayout.ToggleLeft(clip.name, animClipToggle[i], GUILayout.Width(400));
                EditorGUILayout.LabelField($"长度: {clip.length:F2}s", GUILayout.Width(100));
                EditorGUILayout.LabelField($"帧率: {clip.frameRate:F0}fps", GUILayout.Width(100));
                // 添加路径信息
                EditorGUILayout.LabelField($"路径: {AssetDatabase.GetAssetPath(clip)}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }
        }
        
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        // 转换按钮
        GUI.enabled = fbxObject != null && animClips.Count > 0 && !isConverting;
        if (GUILayout.Button("开始批量转换", GUILayout.Height(40)))
        {
            StartBatchConversion();
        }
        GUI.enabled = true;

        // 进度显示
        if (isConverting)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("转换进度：", currentStatus);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), conversionProgress, $"转换中... {conversionProgress * 100:F1}%");
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
                        SaveSession();
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
            }
        }
        EditorGUILayout.LabelField("当前路径:", fbxAssetPath);
    }

    private void LoadSession()
    {
        fbxAssetPath = SessionState.GetString(SessionKeyFBXPath, fbxAssetPath);
        animFolder = SessionState.GetString(SessionKeyAnimDir, animFolder);
        exportFolder = SessionState.GetString(SessionKeyExportDir, exportFolder);
        positionError = SessionState.GetFloat(SessionKeyPositionError, positionError);
        rotationError = SessionState.GetFloat(SessionKeyRotationError, rotationError);
        frameRate = SessionState.GetFloat(SessionKeyFrameRate, frameRate);
        if (!string.IsNullOrEmpty(fbxAssetPath))
            fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
    }

    private void SaveSession()
    {
        SessionState.SetString(SessionKeyFBXPath, fbxAssetPath);
        SessionState.SetString(SessionKeyAnimDir, animFolder);
        SessionState.SetString(SessionKeyExportDir, exportFolder);
        SessionState.SetFloat(SessionKeyPositionError, positionError);
        SessionState.SetFloat(SessionKeyRotationError, rotationError);
        SessionState.SetFloat(SessionKeyFrameRate, frameRate);
    }

    private void RefreshAnimClips()
    {
        animClips.Clear();
        animClipToggle.Clear();
        if (string.IsNullOrEmpty(animFolder)) return;

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new string[] { animFolder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip != null)
            {
                animClips.Add(clip);
                animClipToggle.Add(false);
                Debug.Log($"找到动画剪辑: {clip.name} 在路径: {assetPath}");
            }
        }

        Debug.Log($"总共找到 {animClips.Count} 个动画剪辑");
    }

    private void StartBatchConversion()
    {
        var selectedClips = animClips.Where((c, i) => animClipToggle[i]).ToList();
        if (selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未选择任何动画剪辑", "确定");
            return;
        }

        if (!Directory.Exists(exportFolder))
        {
            Directory.CreateDirectory(exportFolder);
        }

        // 开始批量转换
        EditorCoroutine.Start(BatchConvertAnimations(selectedClips));
    }

    private IEnumerator BatchConvertAnimations(List<AnimationClip> clips)
    {
        isConverting = true;
        conversionProgress = 0f;
        currentStatus = "准备开始转换...";

        int successCount = 0;
        int totalCount = clips.Count;

        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            currentStatus = $"正在转换: {clip.name} ({i + 1}/{totalCount})";
            conversionProgress = (float)i / totalCount;
            
            // 强制重绘界面
            Repaint();
            yield return null;

            try
            {
                bool success = ConvertSingleAnimation(clip);
                if (success)
                {
                    successCount++;
                    Debug.Log($"成功转换动画: {clip.name}");
                }
                else
                {
                    Debug.LogError($"转换动画失败: {clip.name}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"转换动画 {clip.name} 时发生错误：{e.Message}\n{e.StackTrace}");
            }

            // 给编辑器一些时间处理
            yield return new WaitForSeconds(0.1f);
        }

        // 等待所有协程完成
        currentStatus = "等待所有转换完成...";
        Repaint();
        yield return new WaitForSeconds(1f); // 给录制协程一些时间完成

        // 完成
        currentStatus = "转换完成！";
        conversionProgress = 1f;
        Repaint();

        // 刷新资源数据库
        AssetDatabase.Refresh();
        
        // 等待资源刷新完成
        yield return new WaitForSeconds(0.5f);
        
        // 最后弹窗显示结果
        EditorUtility.DisplayDialog("转换完成", $"成功转换 {successCount}/{totalCount} 个动画到:\n{exportFolder}", "确定");

        isConverting = false;
    }

    private bool ConvertSingleAnimation(AnimationClip clip)
    {
        try
        {
            // 创建临时GameObject
            GameObject tempRoot = GameObject.Instantiate(fbxObject);
            tempRoot.name = "TempRoot_" + clip.name;

            // 添加必要的组件
            var animator = tempRoot.GetComponent<Animator>();
            if (animator == null)
                animator = tempRoot.AddComponent<Animator>();

            // 创建临时AnimatorController
            string controllerPath = "Assets/TempController_" + System.Guid.NewGuid().ToString() + ".controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.AddState(clip.name);
            state.motion = clip;
            stateMachine.defaultState = state;
            animator.runtimeAnimatorController = controller;

            // 创建Timeline系统
            var director = tempRoot.GetComponent<PlayableDirector>();
            if (director == null)
                director = tempRoot.AddComponent<PlayableDirector>();

            // 创建TimelineAsset
            var timelineAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            timelineAsset.name = clip.name + "_Timeline";
            
            // 设置Timeline的帧率
            timelineAsset.editorSettings.frameRate = frameRate;

            // 创建AnimationTrack
            var animationTrack = timelineAsset.CreateTrack<AnimationTrack>();
            animationTrack.name = "Animation";
            
            // 绑定轨道到模型 - 这是关键步骤！
            animationTrack.trackOffset = TrackOffset.ApplySceneOffsets;
            
            // 设置轨道的绑定对象 - 这是最重要的！
            animationTrack.avatarMask = null; // 确保没有AvatarMask限制
            
            // 使用正确的方法创建TimelineClip并设置动画剪辑
            var timelineClip = animationTrack.CreateClip(clip);
            if (timelineClip != null)
            {
                timelineClip.duration = clip.length;
                timelineClip.displayName = clip.name;
            }

            // 设置PlayableDirector
            director.playableAsset = timelineAsset;
            director.time = 0;
            
            // 强制刷新Timeline
            director.RebuildGraph();

            // 开始录制 - 使用Timeline系统
            bool recordingSuccess = StartRecording(clip, timelineAsset, tempRoot, controllerPath);

            // 添加调试信息
            Debug.Log($"设置完成 - 动画: {clip.name}, 长度: {clip.length:F3}s, 帧率: {frameRate}, 预期帧数: {Mathf.RoundToInt(clip.length * frameRate)}");
            Debug.Log($"Timeline设置: {timelineAsset.name}, 轨道数: {timelineAsset.GetOutputTracks().Count()}");
            Debug.Log($"AnimationTrack: {animationTrack.name}, TimelineClip数量: {animationTrack.GetClips().Count()}");
            Debug.Log($"PlayableDirector: {director.name}, PlayableAsset: {director.playableAsset?.name}");
            
            // 检查TimelineClip设置
            if (timelineClip != null)
            {
                Debug.Log($"TimelineClip: {timelineClip.displayName}, 持续时间: {timelineClip.duration:F3}s");
                Debug.Log($"TimelineClip动画剪辑: {timelineClip.animationClip?.name ?? "NULL"}");
            }

            return recordingSuccess;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"转换动画 {clip.name} 时发生错误：{e.Message}");
            return false;
        }
    }

    private bool StartRecording(AnimationClip clip, TimelineAsset timelineAsset, GameObject tempRoot, string controllerPath)
    {
        try
        {
            // 创建输出路径
            string outputPath = Path.Combine(exportFolder, clip.name + ".anim");
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            // 开始录制协程
            EditorCoroutine.Start(RecordingCoroutine(clip, timelineAsset, tempRoot, controllerPath, outputPath));
            
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"开始录制失败：{e.Message}");
            return false;
        }
    }

    private IEnumerator RecordingCoroutine(AnimationClip clip, TimelineAsset timelineAsset, GameObject tempRoot, string controllerPath, string outputPath)
    {
        try
        {
            // 获取PlayableDirector组件
            var director = tempRoot.GetComponent<PlayableDirector>();
            if (director == null)
            {
                Debug.LogError("PlayableDirector为空");
                yield break;
            }

            // 参考TimelineRecorder.cs的逻辑，使用clip.length
            var clipFrameCount = Mathf.RoundToInt(clip.length * frameRate);
            
            int frame = 0;
            
            // 等待一帧确保设置生效
            yield return new WaitForEndOfFrame();
            
            // 开始播放Timeline
            director.Play();
            if (director.playableGraph.IsValid())
            {
                director.playableGraph.GetRootPlayable(0).SetSpeed(0);
            }
            
            // 等待一帧确保Timeline开始
            yield return new WaitForEndOfFrame();
            
            // 确保Timeline轨道正确绑定到模型
            if (director.playableGraph.IsValid())
            {
                // 强制重建PlayableGraph以确保绑定
                director.RebuildGraph();
                yield return new WaitForEndOfFrame();
            }
            
            // 确保Timeline窗口和Game窗口都在前台
            var timelineWindowType = System.Type.GetType("UnityEditor.Timeline.TimelineWindow,Unity.Timeline.Editor");
            var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            
            if (timelineWindowType != null)
                EditorWindow.FocusWindowIfItsOpen(timelineWindowType);
            if (gameViewType != null)
                EditorWindow.FocusWindowIfItsOpen(gameViewType);
            
            // 等待窗口聚焦
            yield return new WaitForEndOfFrame();

            // 确保Timeline轨道正确绑定到模型
            var currentTimelineAsset = director.playableAsset as TimelineAsset;
            if (currentTimelineAsset != null)
            {
                var tracks = currentTimelineAsset.GetOutputTracks();
                foreach (var track in tracks)
                {
                    if (track is AnimationTrack animTrack)
                    {
                        // 强制绑定轨道到模型
                        director.SetGenericBinding(animTrack, tempRoot);
                        Debug.Log($"已绑定轨道 {animTrack.name} 到模型 {tempRoot.name}");
                    }
                }
                
                // 重新构建PlayableGraph以应用绑定
                director.RebuildGraph();
                yield return new WaitForEndOfFrame();
            }

            // 创建动画剪辑
            AnimationClip outputClip = new AnimationClip();
            outputClip.name = Path.GetFileNameWithoutExtension(outputPath);
            outputClip.frameRate = frameRate;

            // 创建录制器
            var recorder = new GameObjectRecorder(tempRoot);
            recorder.BindComponentsOfType<Transform>(tempRoot, true);
            
            // 检查录制器设置
            Debug.Log($"录制器设置检查:");
            Debug.Log($"录制目标: {tempRoot?.name}");
            Debug.Log($"录制器绑定状态: {recorder != null}");
            
            // 检查Timeline状态
            Debug.Log($"Timeline状态检查:");
            Debug.Log($"PlayableDirector: {director?.name}");
            Debug.Log($"PlayableAsset: {director?.playableAsset?.name}");
            Debug.Log($"PlayableGraph有效: {director?.playableGraph.IsValid()}");
            Debug.Log($"当前时间: {director?.time:F3}s");
            Debug.Log($"持续时间: {director?.duration:F3}s");
            
            // 检查Timeline轨道绑定
            var timelineAssetForCheck = director.playableAsset as TimelineAsset;
            if (timelineAssetForCheck != null)
            {
                var tracks = timelineAssetForCheck.GetOutputTracks();
                Debug.Log($"Timeline轨道数量: {tracks.Count()}");
                
                foreach (var track in tracks)
                {
                    if (track is AnimationTrack animTrack)
                    {
                        Debug.Log($"AnimationTrack: {animTrack.name}");
                        Debug.Log($"轨道偏移设置: {animTrack.trackOffset}");
                        
                        // 检查轨道绑定
                        var binding = director.GetGenericBinding(animTrack);
                        Debug.Log($"轨道绑定对象: {binding?.name ?? "NULL"}");
                        
                        var trackClips = animTrack.GetClips();
                        Debug.Log($"轨道上的剪辑数量: {trackClips.Count()}");
                        
                        foreach (var trackClip in trackClips)
                        {
                            var clipName = trackClip.animationClip != null ? trackClip.animationClip.name : "NULL";
                            Debug.Log($"剪辑: {trackClip.displayName}, 动画: {clipName}, 持续时间: {trackClip.duration:F3}s");
                        }
                    }
                }
            }

            // 逐帧录制 - 参考TimelineRecorder.cs的逻辑
            while (frame < clipFrameCount)
            {
                // 设置Timeline时间
                director.time = frame / frameRate;
                if (director.playableGraph.IsValid())
                {
                    director.playableGraph.GetRootPlayable(0).SetSpeed(0);
                }
                
                // 等待一帧确保Timeline状态更新
                yield return new WaitForEndOfFrame();
                
                // 录制当前帧
                recorder.TakeSnapshot(1f / frameRate);
                
                // 添加调试信息
                if (frame % 30 == 0) // 每30帧输出一次
                {
                    Debug.Log($"录制进度: {clip.name} - 帧 {frame}/{clipFrameCount}, 时间: {director.time:F3}s");
                    Debug.Log($"当前帧Transform状态检查:");
                    
                    // 检查根对象的Transform状态
                    if (tempRoot != null)
                    {
                        Debug.Log($"根对象位置: {tempRoot.transform.position}, 旋转: {tempRoot.transform.rotation.eulerAngles}");
                        
                        // 检查是否有子对象
                        var children = tempRoot.GetComponentsInChildren<Transform>();
                        Debug.Log($"子对象数量: {children.Length}");
                        
                        if (children.Length > 1) // 除了根对象本身
                        {
                            var firstChild = children[1]; // 第一个子对象
                            Debug.Log($"第一个子对象位置: {firstChild.position}, 旋转: {firstChild.rotation.eulerAngles}");
                        }
                    }
                }
                
                frame++;
            }

            Debug.Log($"录制完成: {clip.name}，总帧数: {clipFrameCount}");

            // 保存动画剪辑
            var options = new CurveFilterOptions();
            options.keyframeReduction = true;
            options.scaleError = 0.05f;
            options.positionError = positionError;
            options.rotationError = rotationError;

            recorder.SaveToClip(outputClip, frameRate, options);

            // 创建资源文件
            AssetDatabase.CreateAsset(outputClip, outputPath);
            AssetDatabase.SaveAssets();
            
            // 等待文件系统完成写入
            yield return new WaitForEndOfFrame();
            
            Debug.Log($"已保存Generic动画: {outputPath}");
        }
        finally
        {
            // 清理临时资源
            Debug.Log($"录制完成，开始清理临时资源...");
            
            // 清理临时AnimatorController
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
                Debug.Log($"已删除临时AnimatorController: {controllerPath}");
            }

            // 清理临时GameObject
            if (tempRoot != null)
            {
                string tempRootName = tempRoot.name; // 先保存名称
                GameObject.DestroyImmediate(tempRoot);
                Debug.Log($"已删除临时GameObject: {tempRootName}");
            }
            
            Debug.Log($"临时资源清理完成");
        }
    }
}

// 编辑器协程支持
public class EditorCoroutine
{
    public static EditorCoroutine Start(IEnumerator routine)
    {
        var coroutine = new EditorCoroutine(routine);
        coroutine.Start();
        return coroutine;
    }

    readonly IEnumerator routine;
    EditorCoroutine(IEnumerator routine)
    {
        this.routine = routine;
    }

    void Start()
    {
        EditorApplication.update += Update;
    }

    public void Stop()
    {
        EditorApplication.update -= Update;
    }

    void Update()
    {
        if (!routine.MoveNext())
        {
            Stop();
        }
    }
}
