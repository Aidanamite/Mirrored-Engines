using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


namespace MirroredEngines
{
    public class Main : Mod
    {
        public Transform prefabHolder;
        public static Main instance;
        public static Dictionary<Item_Base, Item_Base> newItems = new Dictionary<Item_Base, Item_Base>();
        public static HashSet<Item_Base> createdItems = new HashSet<Item_Base>();
        public static List<Object> created = new List<Object>();
        Harmony harmony;
        public override bool CanUnload(ref string message)
        {
            if (loaded && SceneManager.GetActiveScene().name == Raft_Network.GameSceneName && ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            {
                message = "Cannot unload while in a multiplayer";
                return false;
            }
            return base.CanUnload(ref message);
        }
        bool loaded = false;
        public void Awake()
        {
            if (SceneManager.GetActiveScene().name == Raft_Network.GameSceneName && ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            {
                Debug.LogError($"[{name}]: This cannot be loaded while in a multiplayer");
                modlistEntry.modinfo.unloadBtn.GetComponent<Button>().onClick.Invoke();
                return;
            }
            loaded = true;
            instance = this;
            prefabHolder = new GameObject("prefabHolder").transform;
            prefabHolder.gameObject.SetActive(false);
            DontDestroyOnLoad(prefabHolder.gameObject);
            created.Add(prefabHolder.gameObject);
            foreach (var i in ItemManager.GetAllItems())
                CheckItem(i, true);
            ExtraSetup(newItems);
            lastKnown = ItemManager.GetAllItems().Count;
            (harmony = new Harmony("com.aidanamite.MirroredEngines")).PatchAll();
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            if (!loaded)
                return;
            loaded = false;
            harmony?.UnpatchAll(harmony.Id);
            var r = new HashSet<int>();
            foreach (var n in newItems)
            {
                r.Add(n.Value.UniqueIndex);
                if (n.Key)
                    Traverse.Create(n.Key.settings_buildable).Field("mirroredVersion").SetValue(null);
            }
            RemoveItems(r);
            newItems.Clear();
            foreach (var v in created)
                if (v)
                    Destroy(v);
            Log("Mod has been unloaded!");
        }

        int lastKnown = -1;
        void Update()
        {
            if (ItemManager.GetAllItems().Count != lastKnown)
            {
                var r = new HashSet<int>();
                var d = new List<Item_Base>();
                var k = new List<Item_Base>();
                foreach (var p in newItems)
                    if (!p.Key)
                    {
                        r.Add(p.Value.UniqueIndex);
                        d.Add(p.Value);
                        k.Add(p.Key);
                        created.Remove(p.Value);
                        foreach (var b in p.Value.settings_buildable.GetBlockPrefabs())
                            Destroy(b.gameObject);
                    }
                foreach (var i in k)
                    newItems.Remove(i);
                RemoveItems(r);
                foreach (var i in d)
                    Destroy(i);
                var s = new Dictionary<Item_Base, Item_Base>();
                foreach (var i in ItemManager.GetAllItems())
                    if (CheckItem(i, true))
                        s[i] = newItems[i];
                ExtraSetup(s);
                lastKnown = ItemManager.GetAllItems().Count;
            }
        }

        public void RemoveItems(HashSet<int> remove)
        {
            ItemManager.GetAllItems().RemoveAll(x => remove.Contains(x.UniqueIndex));
            foreach (var block in BlockCreator.GetPlacedBlocks().ToArray())
                if (block && block.buildableItem && remove.Contains(block.buildableItem.UniqueIndex))
                    BlockCreator.RemoveBlock(block, null, false);
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockQuadType>())
                Traverse.Create(q).Field("acceptableBlockTypes").GetValue<List<Item_Base>>().RemoveAll(x => remove.Contains(x.UniqueIndex));
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockCollisionMask>())
                Traverse.Create(q).Field("blockTypesToIgnore").GetValue<List<Item_Base>>().RemoveAll(x => remove.Contains(x.UniqueIndex));
        }

        public static bool CheckItem(Item_Base item, bool skipExtraSetup = false)
        {
            if (item && !newItems.ContainsKey(item) && !createdItems.Contains(item) && !item.settings_buildable.MirroredVersion && item.settings_buildable.GetBlockPrefab(0) && item.settings_buildable.GetBlockPrefab(0).GetComponentInChildren<MotorWheel>())
            {
                var n = item.Clone(item.UniqueIndex ^ 0x7FFFFFFF, item.UniqueName + "_Mirrored");
                created.Add(n);
                var ps = n.settings_buildable.GetBlockPrefabs().ToArray();
                for (int j = 0; j < ps.Length; j++)
                {
                    ps[j] = ps[j].MirrorObject();
                    foreach (var g in ps[j].GetComponentsInChildren<Tank_Gauge>(true))
                        g.pointerMaxAngle(g.pointerMinAngle() * 2 - g.pointerMaxAngle());
                    ps[j].buildableItem = n;
                }
                Traverse.Create(n.settings_buildable).Field("blockPrefabs").SetValue(ps);
                newItems[item] = n;
                createdItems.Add(n);
                Traverse.Create(n.settings_buildable).Field("mirroredVersion").SetValue(item);
                Traverse.Create(item.settings_buildable).Field("mirroredVersion").SetValue(n);
                Traverse.Create(n.settings_recipe).Field("_hiddenInResearchTable").SetValue(true);
                Traverse.Create(n.settings_recipe).Field("learnedFromBeginning").SetValue(false);
                Traverse.Create(n.settings_recipe).Field("craftingCategory").SetValue(CraftingCategory.Hidden);
                if (!skipExtraSetup)
                    ExtraSetup(new Dictionary<Item_Base, Item_Base> { {item, n } });
                return true;
            }
            return false;
        }

        public static void ExtraSetup(Dictionary<Item_Base, Item_Base> items)
        {
            foreach (var i in items)
                RAPI.RegisterItem(i.Value, true);
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockQuadType>())
                foreach (var n in items)
                    if (q.AcceptsBlock(n.Key))
                        Traverse.Create(q).Field("acceptableBlockTypes").GetValue<List<Item_Base>>().Add(n.Value);
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockCollisionMask>())
                foreach (var n in items)
                    if (q.IgnoresBlock(n.Key))
                        Traverse.Create(q).Field("blockTypesToIgnore").GetValue<List<Item_Base>>().Add(n.Value);
        }
    }

    [HarmonyPatch(typeof(RAPI),"RegisterItem")]
    static class Patch_RegisterItem
    {
        static void Postfix(Item_Base item)
        {
            Main.CheckItem(item);
        }
    }
    static class ExtentionMethods
    {
        public static Item_Base Clone(this Item_Base source, int uniqueIndex, string uniqueName)
        {
            Item_Base item = ScriptableObject.CreateInstance<Item_Base>();
            item.Initialize(uniqueIndex, uniqueName, source.MaxUses);
            item.settings_buildable = source.settings_buildable.Clone();
            item.settings_consumeable = source.settings_consumeable.Clone();
            item.settings_cookable = source.settings_cookable.Clone();
            item.settings_equipment = source.settings_equipment.Clone();
            item.settings_Inventory = source.settings_Inventory.Clone();
            item.settings_recipe = source.settings_recipe.Clone();
            item.settings_usable = source.settings_usable.Clone();
            return item;
        }

        public static void ReplaceValues(this Component value, object original, object replacement)
        {
            foreach (var c in value.GetComponentsInChildren<Component>())
                (c as object).ReplaceValues(original, replacement);
        }
        public static void ReplaceValues(this GameObject value, object original, object replacement)
        {
            foreach (var c in value.GetComponentsInChildren<Component>())
                (c as object).ReplaceValues(original, replacement);
        }

        public static void ReplaceValues(this object value, object original, object replacement)
        {
            var t = value.GetType();
            while (t != typeof(Object) && t != typeof(object))
            {
                foreach (var f in t.GetFields(~BindingFlags.Default))
                    if (!f.IsStatic && f.GetValue(value) == original)
                        f.SetValue(value, replacement);
                t = t.BaseType;
            }
        }
        static FieldInfo _pointerMaxAngle = typeof(Tank_Gauge).GetField("pointerMaxAngle", ~BindingFlags.Default);
        public static float pointerMaxAngle(this Tank_Gauge tank) => (float)_pointerMaxAngle.GetValue(tank);
        public static void pointerMaxAngle(this Tank_Gauge tank, float value) => _pointerMaxAngle.SetValue(tank, value);

        static FieldInfo _pointerMinAngle = typeof(Tank_Gauge).GetField("pointerMinAngle", ~BindingFlags.Default);
        public static float pointerMinAngle(this Tank_Gauge tank) => (float)_pointerMinAngle.GetValue(tank);
        public static void pointerMinAngle(this Tank_Gauge tank, float value) => _pointerMinAngle.SetValue(tank, value);

        //static Assembly raftAssembly = typeof(Raft).Assembly;

        public static T MirrorObject<T>(this T obj) where T : Component => obj == null ? null : MirrorObject(obj.gameObject).GetComponent<T>();
        public static GameObject MirrorObject(this GameObject obj)
        {
            obj = Object.Instantiate(obj, Main.instance.prefabHolder);
            // Start recording
            var poses = new List<(Transform, Vector3)>();
            var l = new List<Transform>() { obj.transform };
            while (l.Count > 0)
            {
                var nl = new List<Transform>();
                foreach (var t in l)
                    foreach (Transform t2 in t)
                    {
                        nl.Add(t2);
                        poses.Add((t2, obj.transform.InverseTransformPoint(t2.position)));
                    }
                l = nl;
            }
            var cols = new List<(BoxCollider, Vector3)>();
            foreach (var c in obj.GetComponentsInChildren<BoxCollider>())
                cols.Add((c, obj.transform.InverseTransformPoint(c.transform.TransformPoint(c.center))));
            var sm = new List<(SkinnedMeshRenderer, Vector3[])>();
            foreach (var m in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (m.sharedMesh?.isReadable ?? false)
                {
                    var v = m.sharedMesh.vertices.ToArray();
                    for (int i = 0; i < v.Length; i++)
                        v[i] = obj.transform.InverseTransformPoint(m.transform.TransformPoint(v[i]));
                    sm.Add((m, v));
                }
            var mf = new List<(MeshFilter, Vector3[])>();
            foreach (var m in obj.GetComponentsInChildren<MeshFilter>(true))
                if (m.sharedMesh?.isReadable ?? false)
                {
                    var v = m.sharedMesh.vertices.ToArray();
                    for (int i = 0; i < v.Length; i++)
                        v[i] = obj.transform.InverseTransformPoint(m.transform.TransformPoint(v[i]));
                    mf.Add((m, v));
                }
            // Start modifying
            foreach (var t in poses)
            {
                var p = t.Item2;
                p.z = -p.z;
                t.Item1.position = obj.transform.TransformPoint(p);
            }
            foreach (var c in cols)
            {
                var p = c.Item2;
                p.z = -p.z;
                c.Item1.center = c.Item1.transform.InverseTransformPoint(obj.transform.TransformPoint(p));
            }
            foreach (var m in sm)
            {
                var n = Object.Instantiate(m.Item1.sharedMesh);
                Main.created.Add(n);
                n.name = n.name.Replace("(Clone)","") + "_Mirrored";
                var v = m.Item2;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = v[i];
                    p.z = -p.z;
                    v[i] = m.Item1.transform.InverseTransformPoint(obj.transform.TransformPoint(p));
                }
                n.vertices = v;
                var b = n.bindposes;
                var b2 = m.Item1.bones;
                for (int i = 0; i < b.Length; i++)
                    b[i] = b2[i].worldToLocalMatrix * m.Item1.rootBone.localToWorldMatrix;
                var t = n.triangles;
                for (int i = 0; i <= t.Length-3; i+=3)
                {
                    var p = t[i];
                    t[i] = t[i + 1];
                    t[i + 1] = p;
                }
                n.triangles = t;
                n.CopySubmeshSettingsOf(m.Item1.sharedMesh);
                n.RecalculateBounds();
                n.RecalculateNormals();
                n.RecalculateTangents();
                m.Item1.sharedMesh = n;
            }
            foreach (var m in mf)
            {
                var n = Object.Instantiate(m.Item1.sharedMesh);
                Main.created.Add(n);
                n.name = n.name.Replace("(Clone)", "") + "_Mirrored";
                var v = m.Item2;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = v[i];
                    p.z = -p.z;
                    v[i] = m.Item1.transform.InverseTransformPoint(obj.transform.TransformPoint(p));
                }
                n.vertices = v;
                var t = n.triangles;
                for (int i = 0; i <= t.Length - 3; i += 3)
                {
                    var p = t[i];
                    t[i] = t[i + 1];
                    t[i + 1] = p;
                }
                n.triangles = t;
                n.CopySubmeshSettingsOf(m.Item1.sharedMesh);
                n.RecalculateBounds();
                n.RecalculateNormals();
                n.RecalculateTangents();
                m.Item1.sharedMesh = n;
            }
            return obj;
        }

        public static void CopySubmeshSettingsOf(this Mesh target, Mesh source)
        {
            var c = target.subMeshCount = source.subMeshCount;
            for (var i = 0; i < c; i++)
                target.SetSubMesh(i, source.GetSubMesh(i), ~UnityEngine.Rendering.MeshUpdateFlags.Default);
        }
    }
}