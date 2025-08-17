using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TimelineRecorder : MonoBehaviour {
  [SerializeField] public PlayableDirector director;
  [SerializeField] public GameObject rootBone;
  [HideInInspector] public GameObjectRecorder m_Recorder;

  // 0.05f : low compression, slow, high quality
  // 0.1f  : high compression, fast, low quality
  [SerializeField] public float positionError = 0.05f;
  [SerializeField] public float rotationError = 0.05f;
}

#if UNITY_EDITOR
[CustomEditor(typeof(TimelineRecorder))]
[CanEditMultipleObjects]
public class TimelineRecorderCustomEditor : Editor {
  static TimelineRecorder Instance;

  public override void OnInspectorGUI() {
    base.OnInspectorGUI();
    Instance = target as TimelineRecorder;

    EditorGUILayout.HelpBox("SHOW THE GAME WINDOW IN THE FOREGROUND", MessageType.Warning, true);

    EditorGUILayout.Space(10);
    if (GUILayout.Button("\nRecording\n")) {
      Instance.StartCoroutine(RecordingTimeline());
    }
  }

  private IEnumerator RecordingTimeline() {
    var timelineAsset = Instance.director.playableAsset as TimelineAsset;
    var clipFrameCount = Mathf.RoundToInt((float) Instance.director.duration * (float) timelineAsset.editorSettings.frameRate);

    int frame = 0;
    Instance.director.time = 0;

    yield return new WaitForSeconds(1);
    Instance.director.Play();
    Instance.director.playableGraph.GetRootPlayable(0).SetSpeed(0);

    AnimationClip clip = new AnimationClip();
    clip.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(timelineAsset));
    clip.frameRate = 60;

    Instance.m_Recorder = new GameObjectRecorder(Instance.rootBone);
    Instance.m_Recorder.BindComponentsOfType<Transform>(Instance.rootBone, true);

    while (frame < clipFrameCount) {
      Instance.director.time = frame / timelineAsset.editorSettings.frameRate;
      Instance.director.playableGraph.GetRootPlayable(0).SetSpeed(0);
      yield return new WaitForEndOfFrame();
      Instance.m_Recorder.TakeSnapshot(0.01666666666666667f);
      Debug.Log(frame + ": done");
      frame++;
    }

    Debug.Log("finished");

    var options = new CurveFilterOptions();
    options.keyframeReduction = true;
    options.scaleError = 0.05f;
    options.positionError = Instance.positionError;
    options.rotationError = Instance.rotationError;

    Instance.m_Recorder.SaveToClip(clip, 60, options);

    var assetPath = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(timelineAsset))}/{clip.name}.anim";
    if (File.Exists(assetPath)) {
      AssetDatabase.DeleteAsset(assetPath);
    }

    AssetDatabase.CreateAsset(clip, assetPath);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
  }
}
#endif