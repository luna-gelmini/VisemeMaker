using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VisemeMaker
{
    public enum Vowel { A, I, U, E, O }

    // Builds VRChat's 14 visemes by blending a model's vowel shapes.
    public static class VisemeMaker
    {
        public const string Prefix = "vrc.v_";

        // VRChat's viseme order. sil stays empty: VRChat shows the neutral face for it.
        public static readonly string[] Visemes = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };

        static readonly Dictionary<Vowel, string[]> Names = new Dictionary<Vowel, string[]>
        {
            { Vowel.A, new[] { "a", "ah", "aa", "あ", "ア" } },
            { Vowel.I, new[] { "i", "ih", "ch", "ee", "い", "イ" } },
            { Vowel.U, new[] { "u", "uu", "ou", "oo", "う", "ウ" } },
            { Vowel.E, new[] { "e", "eh", "え", "エ" } },
            { Vowel.O, new[] { "o", "oh", "お", "オ" } },
        };

        static readonly Regex Decoration = new Regex(@"^(blendshape\d*\.|vrc\.v_|v_|fcl_mth_|mth_|mouth_|mouth|lip_|lips_|viseme_)|(_mouth|_mth|_l|_r|\.\d+)$", RegexOptions.IgnoreCase);

        // Finds the shape for each vowel by name, ignoring common prefixes like mouth_ or Fcl_MTH_.
        public static Dictionary<Vowel, string> Detect(Mesh mesh)
        {
            var found = new Dictionary<Vowel, string>();
            if (mesh == null) return found;
            var shapes = new List<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var n = mesh.GetBlendShapeName(i);
                if (!n.StartsWith(Prefix)) shapes.Add(n);
            }
            foreach (var vowel in Names.Keys)
            {
                foreach (var candidate in Names[vowel])
                {
                    var match = shapes.FirstOrDefault(s => Clean(s) == candidate);
                    if (match != null) { found[vowel] = match; break; }
                }
            }
            return found;
        }

        static string Clean(string name)
        {
            var n = name.Trim().ToLowerInvariant();
            for (int i = 0; i < 3; i++) n = Decoration.Replace(n, "");
            return n.Trim('_', '.', ' ', '-');
        }

        // Weights of each vowel in each viseme. Missing U, E or I fall back to the others.
        public static Dictionary<Vowel, float> Recipe(string viseme, ICollection<Vowel> have)
        {
            bool i = have.Contains(Vowel.I), u = have.Contains(Vowel.U), e = have.Contains(Vowel.E);
            var I = i ? Vowel.I : Vowel.E;
            var r = new Dictionary<Vowel, float>();
            switch (viseme)
            {
                case "aa": r[Vowel.A] = 1f; break;
                case "ih": if (i && e) { r[Vowel.I] = 0.6f; r[Vowel.E] = 0.2f; } else { r[I] = 0.7f; r[Vowel.A] = 0.1f; } break;
                case "E": if (e) r[Vowel.E] = 1f; else { r[I] = 0.7f; r[Vowel.O] = 0.3f; } break;
                case "oh": r[Vowel.O] = 1f; break;
                case "ou": if (u) r[Vowel.U] = 1f; else r[Vowel.O] = 0.9f; break;
                case "PP": r[Vowel.A] = 0.05f; r[Vowel.O] = 0.05f; break;
                case "FF": r[Vowel.A] = 0.2f; r[I] = 0.4f; break;
                case "TH": r[Vowel.A] = 0.4f; r[Vowel.O] = 0.15f; break;
                case "DD": r[Vowel.A] = 0.3f; r[I] = 0.7f; break;
                case "kk": r[Vowel.A] = 0.7f; r[I] = 0.4f; break;
                case "CH": r[I] = 1f; break;
                case "SS": r[I] = 0.8f; break;
                case "nn": r[Vowel.A] = 0.2f; r[I] = 0.7f; break;
                case "RR": r[I] = 0.5f; r[Vowel.O] = 0.3f; break;
            }
            return r;
        }

        public static bool CanBuild(Dictionary<Vowel, string> map, out string why)
        {
            why = null;
            if (!map.ContainsKey(Vowel.A)) why = "Needs an A shape.";
            else if (!map.ContainsKey(Vowel.O)) why = "Needs an O shape.";
            else if (!map.ContainsKey(Vowel.I) && !map.ContainsKey(Vowel.E)) why = "Needs an I (or Ch) or an E shape.";
            return why == null;
        }

        // Writes the visemes into a copy of the renderer's mesh (or the existing copy, replacing
        // old visemes) and returns the 15 names for the avatar descriptor.
        public static string[] Build(SkinnedMeshRenderer smr, Dictionary<Vowel, string> map, float strength)
        {
            var src = smr.sharedMesh;
            var mesh = OwnedCopy(src);
            int n = mesh.vertexCount;

            var keep = new List<KeyValuePair<string, List<KeyValuePair<float, Vector3[][]>>>>();
            var weights = new Dictionary<string, float>();
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                var name = mesh.GetBlendShapeName(s);
                weights[name] = smr.sharedMesh == mesh ? smr.GetBlendShapeWeight(s) : GetWeight(smr, name);
                if (name.StartsWith(Prefix)) continue;
                var frames = new List<KeyValuePair<float, Vector3[][]>>();
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
                    mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    frames.Add(new KeyValuePair<float, Vector3[][]>(mesh.GetBlendShapeFrameWeight(s, f), new[] { dv, dn, dt }));
                }
                keep.Add(new KeyValuePair<string, List<KeyValuePair<float, Vector3[][]>>>(name, frames));
            }

            var vowelDeltas = new Dictionary<Vowel, Vector3[][]>();
            foreach (var kv in map)
            {
                var frames = keep.First(k => k.Key == kv.Value).Value;
                vowelDeltas[kv.Key] = frames[frames.Count - 1].Value;
            }

            mesh.ClearBlendShapes();
            foreach (var sh in keep)
                foreach (var f in sh.Value)
                    mesh.AddBlendShapeFrame(sh.Key, f.Key, f.Value[0], f.Value[1], f.Value[2]);

            var names = new string[Visemes.Length];
            names[0] = "-none-";
            for (int v = 1; v < Visemes.Length; v++)
            {
                var recipe = Recipe(Visemes[v], vowelDeltas.Keys);
                var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
                foreach (var part in recipe)
                {
                    Vector3[][] d;
                    if (!vowelDeltas.TryGetValue(part.Key, out d)) continue;
                    float w = part.Value * strength;
                    for (int i = 0; i < n; i++)
                    {
                        dv[i] += d[0][i] * w;
                        dn[i] += d[1][i] * w;
                        dt[i] += d[2][i] * w;
                    }
                }
                names[v] = Prefix + Visemes[v];
                mesh.AddBlendShapeFrame(names[v], 100f, dv, dn, dt);
            }

            Undo.RecordObject(smr, "Make visemes");
            smr.sharedMesh = mesh;
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                float w;
                smr.SetBlendShapeWeight(s, weights.TryGetValue(mesh.GetBlendShapeName(s), out w) && !mesh.GetBlendShapeName(s).StartsWith(Prefix) ? w : 0f);
            }
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            return names;
        }

        static float GetWeight(SkinnedMeshRenderer smr, string name)
        {
            int i = smr.sharedMesh.GetBlendShapeIndex(name);
            return i >= 0 ? smr.GetBlendShapeWeight(i) : 0f;
        }

        // Model meshes (FBX sub-assets) can't be edited, so visemes go into a copy saved next to
        // the model. A copy made earlier is reused.
        static Mesh OwnedCopy(Mesh src)
        {
            var path = AssetDatabase.GetAssetPath(src);
            if (path.EndsWith(".asset") && AssetDatabase.LoadMainAssetAtPath(path) == src) return src;
            var dir = string.IsNullOrEmpty(path) ? "Assets" : Path.GetDirectoryName(path).Replace('\\', '/');
            var copyPath = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + Safe(src.name) + "_visemes.asset");
            var copy = Object.Instantiate(src);
            copy.name = src.name + "_visemes";
            AssetDatabase.CreateAsset(copy, copyPath);
            return copy;
        }

        static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
