using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VisemeMaker
{
    public class VisemeMakerWindow : EditorWindow
    {
        GameObject avatar;
        SkinnedMeshRenderer face;
        Dictionary<Vowel, string> map = new Dictionary<Vowel, string>();
        float strength = 1f;
        int preview;
        float[] savedWeights;
        GUIStyle small;

        [MenuItem("Tools/Viseme Maker")]
        static void Open()
        {
            GetWindow<VisemeMakerWindow>("Viseme Maker").minSize = new Vector2(360, 320);
        }

        void OnDisable()
        {
            StopPreview();
        }

        void OnGUI()
        {
            if (small == null) small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            EditorGUILayout.Space(4);

            var a = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (a != avatar)
            {
                StopPreview();
                avatar = a;
                face = avatar != null ? GuessFace(avatar) : null;
                map = face != null ? VisemeMaker.Detect(face.sharedMesh) : new Dictionary<Vowel, string>();
            }
            if (avatar == null)
            {
                EditorGUILayout.HelpBox("Pick an avatar. Viseme Maker blends its vowel shapes (A, I, U, E, O) into VRChat's 14 visemes and sets up lip sync.", MessageType.Info);
                return;
            }

            var meshes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList();
            if (meshes.Count == 0)
            {
                EditorGUILayout.HelpBox("No mesh on this avatar has shape keys.", MessageType.Warning);
                return;
            }
            int fi = Mathf.Max(0, meshes.IndexOf(face));
            int nfi = EditorGUILayout.Popup("Face mesh", fi, meshes.Select(m => m.name + " (" + m.sharedMesh.blendShapeCount + " shapes)").ToArray());
            if (nfi != fi || face == null)
            {
                StopPreview();
                face = meshes[nfi];
                map = VisemeMaker.Detect(face.sharedMesh);
            }

            EditorGUILayout.Space(6);
            GUILayout.Label("Vowel shapes", EditorStyles.boldLabel);
            var options = new List<string> { "(none)" };
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
            {
                var n = face.sharedMesh.GetBlendShapeName(i);
                if (!n.StartsWith(VisemeMaker.Prefix)) options.Add(n);
            }
            foreach (Vowel v in System.Enum.GetValues(typeof(Vowel)))
            {
                string current;
                map.TryGetValue(v, out current);
                int idx = current != null ? Mathf.Max(0, options.IndexOf(current)) : 0;
                int pick = EditorGUILayout.Popup(v == Vowel.I ? "I (or Ch)" : v.ToString(), idx, options.ToArray());
                if (pick != idx)
                {
                    if (pick == 0) map.Remove(v); else map[v] = options[pick];
                    if (preview > 0) ApplyPreview();
                }
            }
            GUILayout.Label("Detected from the shape names. A and O are required, plus I or E; U and E make ou and E more accurate.", small);

            EditorGUILayout.Space(6);
            float s = EditorGUILayout.Slider("Strength", strength, 0.3f, 1.5f);
            if (!Mathf.Approximately(s, strength)) { strength = s; if (preview > 0) ApplyPreview(); }

            var names = new[] { "(off)" }.Concat(VisemeMaker.Visemes.Skip(1)).ToArray();
            int p = EditorGUILayout.Popup("Preview", preview, names);
            if (p != preview)
            {
                preview = p;
                if (preview == 0) StopPreview(); else ApplyPreview();
            }

            string why;
            bool ok = VisemeMaker.CanBuild(map, out why);
            if (!ok) EditorGUILayout.HelpBox(why, MessageType.Warning);
            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(!ok))
                if (GUILayout.Button("Create visemes", GUILayout.Height(28))) Create();
        }

        static SkinnedMeshRenderer GuessFace(GameObject avatar)
        {
            var desc = avatar.GetComponent<VRCAvatarDescriptor>();
            if (desc != null && desc.VisemeSkinnedMesh != null) return desc.VisemeSkinnedMesh;
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .OrderByDescending(r => VisemeMaker.Detect(r.sharedMesh).Count)
                .ThenByDescending(r => r.sharedMesh.blendShapeCount)
                .FirstOrDefault();
        }

        // Previews a viseme by setting the source vowel weights on the renderer itself.
        void ApplyPreview()
        {
            var mesh = face.sharedMesh;
            if (savedWeights == null)
            {
                savedWeights = new float[mesh.blendShapeCount];
                for (int i = 0; i < savedWeights.Length; i++) savedWeights[i] = face.GetBlendShapeWeight(i);
            }
            for (int i = 0; i < savedWeights.Length; i++) face.SetBlendShapeWeight(i, savedWeights[i]);
            foreach (var part in VisemeMaker.Recipe(VisemeMaker.Visemes[preview], map.Keys))
            {
                string shape;
                if (!map.TryGetValue(part.Key, out shape)) continue;
                int idx = mesh.GetBlendShapeIndex(shape);
                if (idx >= 0) face.SetBlendShapeWeight(idx, Mathf.Clamp(savedWeights[idx] + part.Value * strength * 100f, 0f, 150f));
            }
            SceneView.RepaintAll();
        }

        void StopPreview()
        {
            if (face != null && savedWeights != null && face.sharedMesh != null && savedWeights.Length == face.sharedMesh.blendShapeCount)
                for (int i = 0; i < savedWeights.Length; i++) face.SetBlendShapeWeight(i, savedWeights[i]);
            savedWeights = null;
            preview = 0;
            SceneView.RepaintAll();
        }

        void Create()
        {
            StopPreview();
            var names = VisemeMaker.Build(face, map, strength);
            var desc = avatar.GetComponent<VRCAvatarDescriptor>();
            if (desc != null)
            {
                Undo.RecordObject(desc, "Set up lip sync");
                desc.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                desc.VisemeSkinnedMesh = face;
                desc.VisemeBlendShapes = names;
                EditorUtility.SetDirty(desc);
            }
            ShowNotification(new GUIContent("Created 14 visemes" + (desc != null ? " and set up lip sync" : "")));
        }
    }
}
