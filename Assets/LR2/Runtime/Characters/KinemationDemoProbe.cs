#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using LR2.AI;
using LR2.Combat;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Differential test (editor only, flag <c>-lr2DemoProbe</c>): spawns Kinemation's STOCK humanoid demo player next to
    /// the LR2 soldier, triggers a reload on both and logs the same bones, curves and every Kinemation input property for
    /// each, so anything LR2's controller does differently from the demo controller shows up side by side.
    /// Output: Logs/LR2_DemoProbe/report.txt
    /// </summary>
    [DefaultExecutionOrder(1250)]
    public class KinemationDemoProbe : MonoBehaviour
    {
        private const string DemoPrefab = "Assets/KinemationDemo/Prefabs/Humanoid/FPSHumanoidPlayer.prefab";
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly StringBuilder _log = new StringBuilder();

        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-lr2DemoProbe") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Requested || FindAnyObjectByType<KinemationDemoProbe>() != null) return;
            var go = new GameObject("[LR2 DemoProbe]");
            DontDestroyOnLoad(go);
            go.AddComponent<KinemationDemoProbe>();
        }

        private void Note(string s) => _log.AppendLine($"[{Time.time,7:0.00}] {s}");

        private IEnumerator Start()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LR2_DemoProbe"));
            Directory.CreateDirectory(outDir);
            Application.logMessageReceived += (m, s, t) => { if (t == LogType.Exception || t == LogType.Error) if (!m.Contains("Plastic")) Note($"{t}: {m} | {(s ?? "").Split('\n')[0]}"); };

            float end = Time.time + 6f;
            SoldierController lr2 = null;
            while (lr2 == null && Time.time < end)
            {
                var c = Combatant.FindLocalPlayer();
                lr2 = c != null ? c.GetComponent<SoldierController>() : null;
                yield return null;
            }
            yield return new WaitForSeconds(3f);
            foreach (var bot in FindObjectsByType<BotController>()) bot.enabled = false;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DemoPrefab);
            if (prefab == null) { Note("demo prefab missing"); Write(outDir); yield break; }
            Vector3 pos = lr2 != null ? lr2.transform.position + lr2.transform.right * 4f : Vector3.up * 0.1f;
            var demo = Instantiate(prefab, pos, lr2 != null ? lr2.transform.rotation : Quaternion.identity);
            demo.name = "STOCK_FPSHumanoidPlayer";
            foreach (var cam in demo.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var al in demo.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
            Note($"Spawned stock demo player at {pos}; LR2 soldier = {(lr2 != null ? lr2.name : "none")}");
            yield return new WaitForSeconds(3f);

            Dump("idle", demo, lr2 != null ? lr2.gameObject : null);

            // reload both
            var demoCtrl = demo.GetComponents<MonoBehaviour>().FirstOrDefault(m => m.GetType().Name == "FPSController");
            var reload = demoCtrl != null ? demoCtrl.GetType().GetMethod("OnReload", F) : null;
            if (reload != null) reload.Invoke(demoCtrl, null); else Note("demo OnReload not found");
            if (lr2 != null && lr2.ActiveFirearm != null)
            {
                lr2.ActiveFirearm.OnFirePressed();
                yield return null;
                lr2.ActiveFirearm.OnFireReleased();
                lr2.RequestReload();
            }
            foreach (float t in new[] { 0.3f, 0.7f, 1.0f, 1.6f, 2.2f })
            {
                yield return new WaitForSeconds(t - _last);
                _last = t;
                Dump($"reload +{t:0.0}s", demo, lr2 != null ? lr2.gameObject : null);
            }
            Write(outDir);
        }

        private float _last;

        private void Dump(string label, GameObject demo, GameObject lr2)
        {
            var sb = new StringBuilder($"===== {label} =====");
            var a = Describe(demo);
            var b = lr2 != null ? Describe(lr2) : new Dictionary<string, string>();
            foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k))
            {
                a.TryGetValue(key, out var va);
                b.TryGetValue(key, out var vb);
                bool diff = va != vb;
                if (key.StartsWith("input.") && !diff) continue; // only differing inputs, all bones/curves
                sb.Append($"\n  {(diff ? "*" : " ")} {key,-34} STOCK={va ?? "-",-44} LR2={vb ?? "-"}");
            }
            Note(sb.ToString());
        }

        private static Dictionary<string, string> Describe(GameObject root)
        {
            var d = new Dictionary<string, string>();
            var cam = FindDeep(root.transform, "Camera");
            foreach (var n in new[] { "WeaponBone", "IK_Hand_Left", "IK_Hand_Right", "IK WeaponBone", "IK LeftHand", "IK RightHand", "Left_Hand", "Right_Hand" })
            {
                var t = FindDeep(root.transform, n);
                if (t == null) continue;
                Vector3 p = cam != null ? cam.InverseTransformPoint(t.position) : t.position;
                d["bone." + n] = $"cam({p.x:0.000},{p.y:0.000},{p.z:0.000})";
            }
            var pc = root.GetComponent<IPlayablesController>();
            if (pc != null)
                foreach (var c in new[] { "MaskAttachHand", "WeaponBoneWeight", "MaskLeftHand", "FullBodyWeight" })
                    try { d["curve." + c] = pc.GetCurveValue(c).ToString("0.00"); } catch (Exception) { }
            var ui = root.GetComponent<UserInputController>();
            if (ui != null)
            {
                var map = typeof(UserInputController).GetField("_inputPropertyMap", F)?.GetValue(ui) as Dictionary<string, int>;
                var vals = typeof(UserInputController).GetField("_inputProperties", F)?.GetValue(ui) as List<object>;
                if (map != null && vals != null)
                    foreach (var kv in map)
                        if (kv.Value >= 0 && kv.Value < vals.Count)
                            d["input." + kv.Key] = Fmt(vals[kv.Value]);
            }
            var anim = root.GetComponent<Animator>();
            if (anim != null)
            {
                foreach (var p in anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Float) d["anim." + p.name] = anim.GetFloat(p.name).ToString("0.00");
                    else if (p.type == AnimatorControllerParameterType.Bool) d["anim." + p.name] = anim.GetBool(p.name).ToString();
            }
            return d;
        }

        private static string Fmt(object v) => v switch
        {
            float f => f.ToString("0.00"),
            Vector4 v4 => $"({v4.x:0.00},{v4.y:0.00},{v4.z:0.00},{v4.w:0.00})",
            null => "null",
            _ => v.ToString()
        };

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                if (c.name.StartsWith("SWAT_Visual")) continue;
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private void Write(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "report.txt"), "=== KINEMATION STOCK vs LR2 ===\n" + _log);
            Debug.Log("[LR2] DemoProbe finished: " + dir);
        }
    }
}
#endif
