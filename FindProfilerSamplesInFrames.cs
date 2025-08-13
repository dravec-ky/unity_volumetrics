#if UNITY_2020_1_OR_NEWER
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor.Profiling;

public class FindProfilerSamplesInFrames : EditorWindow
{
    [MenuItem("Window/Analysis/SampleFinder")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<FindProfilerSamplesInFrames>();
    }

    string m_SampleName = "";

    List<Vector2Int> m_FoundFrames = new List<Vector2Int>();
    private void OnGUI()
    {
        m_SampleName = EditorGUILayout.TextField(m_SampleName);
        if (GUILayout.Button("Search"))
        {
            m_FoundFrames.Clear();
            for (int frame = ProfilerDriver.firstFrameIndex; frame < ProfilerDriver.lastFrameIndex; frame++)
            {
                var threadIndex = 0;
                var frameData = ProfilerDriver.GetRawFrameDataView(frame, threadIndex);
                while (frameData.valid)
                {
                    var markerId = frameData.GetMarkerId(m_SampleName);
                    if (markerId < 0)
                        break; // marker not present in this frame and thread. skip it.

                    for (int sampleIndex = 0; sampleIndex < frameData.sampleCount; sampleIndex++)
                    {
                        if(frameData.GetSampleMarkerId(sampleIndex) == markerId)
                        {
                            m_FoundFrames.Add(new Vector2Int(frame, threadIndex));
                            break;
                        }
                    }
                    frameData = ProfilerDriver.GetRawFrameDataView(frame, ++threadIndex);
                    frameData.Dispose();
                }
                frameData.Dispose();
            }
        }
#if UNITY_2021_1_OR_NEWER

        GUILayout.BeginHorizontal();
        for (int i = 0; i < m_FoundFrames.Count; i++)
        {
            if(i % 10 == 0)
            {
                // line break
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
            if(GUILayout.Button($"Frame: { m_FoundFrames[i].x} - Thread: { m_FoundFrames[i].y}"))
            {
                var window = GetWindow<ProfilerWindow>();
                var cpuModule = window.GetFrameTimeViewSampleSelectionController(ProfilerWindow.cpuModuleIdentifier);
                using (var frameData = ProfilerDriver.GetRawFrameDataView(m_FoundFrames[i].x, m_FoundFrames[i].y))
                {
                    cpuModule.SetSelection(m_SampleName, m_FoundFrames[i].x, threadId: frameData.threadId);
                }
            }

        }
        GUILayout.EndHorizontal();
#else
        var sb = new StringBuilder();
        foreach (var frame in m_FoundFrames)
        {
            sb.AppendFormat("Frame: {0} - Thread: {1} | ", frame.x, frame.y);
        }

        GUILayout.TextArea(sb.ToString());
#endif
    }
}
#endif
