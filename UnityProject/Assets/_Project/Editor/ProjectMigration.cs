using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityLab.Menu;
using UnityLab.Racing.AI;
using UnityLab.Racing.Cars;
using UnityLab.Racing.Online;
using UnityLab.Racing.Race;
using UnityLab.RTS.Army;
using UnityLab.RTS.UI;
using UnityLab.RTS.World;
using UnityLab.Shared;
using Kind = UnityLab.EditorTools.ProceduralMeshes.Kind;
using Object = UnityEngine.Object;

namespace UnityLab.EditorTools
{
    /// <summary>
    /// One-off migration that turned the two original course projects into this single Unity 6
    /// project. It ran once in CI (batch mode); its output is what the repository contains now.
    /// It is kept as documentation of how the assets were produced.
    ///
    /// Steps: replace every third-party model and material with procedural ones of the same size,
    /// build the online race scene from the single-player one, build the main menu, bake the RTS
    /// NavMesh with the AI Navigation package, register the scenes in the build and re-serialize
    /// every asset in the Unity 6 format.
    /// </summary>
    public static class ProjectMigration
    {
        const string ThirdPartyRoot = "Assets/ThirdParty";
        const string GeneratedMeshes = "Assets/_Project/Shared/Art/Meshes";
        const string GeneratedMaterials = "Assets/_Project/Shared/Art/Materials";
        const string SinglePlayerScene = "Assets/_Project/Racing/Scenes/SinglePlayer.unity";
        const string OnlineScene = "Assets/_Project/Racing/Scenes/Online.unity";
        const string BattlefieldScene = "Assets/_Project/RTS/Scenes/Battlefield.unity";
        const string MenuScene = "Assets/_Project/Menu/Scenes/MainMenu.unity";
        const string RemoteCarPrefab = "Assets/_Project/Racing/Prefabs/CarOnline.prefab";

        static readonly Dictionary<Mesh, Mesh> replacements = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Mesh, Kind> generatedKinds = new Dictionary<Mesh, Kind>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly List<string> report = new List<string>();

        /// <summary>Entry point for CI: <c>-executeMethod UnityLab.EditorTools.ProjectMigration.RunFromCommandLine</c>.</summary>
        public static void RunFromCommandLine()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllLines("migration-report.txt", report.Append("FAILED: " + exception));
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Entry point for the follow-up step: <c>-executeMethod UnityLab.EditorTools.ProjectMigration.RunFollowUpFromCommandLine</c>.</summary>
        public static void RunFollowUpFromCommandLine()
        {
            try
            {
                FollowUp();
                File.WriteAllLines("migration-report.txt", report);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllLines("migration-report.txt", report.Append("FAILED: " + exception));
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Second pass, after the first smoke test: the gate ring had been fitted lying on its side
        /// (a solid bar across the track that stopped every car), so it is rebuilt as an upright,
        /// collider-free hoop; the AI cars also get readable names.
        /// </summary>
        [MenuItem("Unity Lab/Maintenance/Run migration follow-up")]
        public static void FollowUp()
        {
            report.Clear();
            const string ringPath = GeneratedMeshes + "/Ring_10.asset";
            var ring = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
            if (ring == null)
                throw new InvalidOperationException("Missing " + ringPath);

            // The original hoop was 8.69 m in radius and 0.35 m thick, upright and facing +Z.
            Mesh rebuilt = ProceduralMeshes.Build(Kind.Ring, new Bounds(Vector3.zero, new Vector3(17.38f, 17.38f, 0.695f)), ring.name);
            ring.Clear();
            EditorUtility.CopySerialized(rebuilt, ring);
            Object.DestroyImmediate(rebuilt);
            EditorUtility.SetDirty(ring);
            AssetDatabase.SaveAssets();
            Log($"Rebuilt {ringPath} as an upright hoop ({ring.bounds.size}).");

            var names = new Dictionary<string, string> { { "Car_AI_1", "Blue" }, { "Car_AI_2", "Green" }, { "Car_AI_3", "Yellow" } };
            foreach (string path in new[] { SinglePlayerScene, OnlineScene })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int colliders = 0, renamed = 0;
                foreach (MeshCollider collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
                {
                    if (collider.sharedMesh == ring)
                    {
                        Object.DestroyImmediate(collider);
                        colliders++;
                    }
                }
                foreach (Competitor competitor in Object.FindObjectsByType<Competitor>(FindObjectsInactive.Include))
                {
                    if (names.TryGetValue(competitor.name, out string nickname))
                    {
                        competitor.Nickname = nickname;
                        EditorUtility.SetDirty(competitor);
                        renamed++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Log($"{path}: removed {colliders} ring colliders, named {renamed} AI cars.");
            }
        }

        [MenuItem("Unity Lab/Maintenance/Run project migration")]
        public static void Run()
        {
            report.Clear();
            replacements.Clear();
            generatedKinds.Clear();
            materials.Clear();

            CreateFolders();
            CreateMaterials();
            MigratePrefabs();
            MigrateSinglePlayerScene();
            CreateOnlineScene();
            MigrateBattlefield();
            CreateMenuScene();
            RegisterBuildScenes();
            DeleteObsoleteAssets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssetDatabase.ForceReserializeAssets();
            Log("Re-serialized every asset in the current Unity format.");
            File.WriteAllLines("migration-report.txt", report);
            Debug.Log("[Migration] Done:\n" + string.Join("\n", report));
        }

        // ------------------------------------------------------------------ setup

        static void CreateFolders()
        {
            foreach (string folder in new[] { GeneratedMeshes, GeneratedMaterials, "Assets/_Project/Menu/Scenes" })
            {
                string[] parts = folder.Split('/');
                string path = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = path + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(path, parts[i]);
                    path = next;
                }
            }
        }

        static void CreateMaterials()
        {
            Material Make(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
            {
                string path = $"{GeneratedMaterials}/M_{key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Standard"));
                    AssetDatabase.CreateAsset(material, path);
                }
                material.color = color;
                material.SetFloat("_Glossiness", smoothness);
                material.SetFloat("_Metallic", metallic);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                materials[key] = material;
                return material;
            }

            Make("CarPlayer", new Color(0.86f, 0.18f, 0.16f), 0.65f, 0.2f);
            Make("CarAI1", new Color(0.16f, 0.45f, 0.85f), 0.65f, 0.2f);
            Make("CarAI2", new Color(0.2f, 0.66f, 0.33f), 0.65f, 0.2f);
            Make("CarAI3", new Color(0.95f, 0.72f, 0.15f), 0.65f, 0.2f);
            Make("CarRemote", new Color(0.88f, 0.88f, 0.9f), 0.65f, 0.2f);
            Make("Tyre", new Color(0.09f, 0.09f, 0.1f), 0.15f);
            Make("Unit", new Color(0.92f, 0.92f, 0.92f), 0.2f);
            Make("Building", new Color(0.78f, 0.69f, 0.56f), 0.1f);
            Make("Trunk", new Color(0.45f, 0.31f, 0.2f), 0.05f);
            Make("Foliage", new Color(0.29f, 0.56f, 0.27f), 0.05f);
            Make("Rock", new Color(0.55f, 0.55f, 0.58f), 0.1f);
            Make("Ring", new Color(0.98f, 0.62f, 0.16f), 0.5f);
            Make("Prop", new Color(0.7f, 0.7f, 0.72f), 0.2f);
            Log($"Created {materials.Count} materials in {GeneratedMaterials}.");
        }

        // ------------------------------------------------------------------ third-party replacement

        static bool IsThirdParty(Object asset) =>
            asset != null && AssetDatabase.GetAssetPath(asset).StartsWith(ThirdPartyRoot, StringComparison.Ordinal);

        static Mesh ReplacementFor(Mesh original)
        {
            if (replacements.TryGetValue(original, out Mesh existing))
                return existing;

            string sourceName = original.name + " " + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(original));
            Kind kind = ProceduralMeshes.Classify(sourceName);
            int index = replacements.Count + 1;
            string name = $"{kind}_{index:00}";
            Mesh mesh = ProceduralMeshes.Build(kind, original.bounds, name);
            AssetDatabase.CreateAsset(mesh, $"{GeneratedMeshes}/{name}.asset");

            replacements[original] = mesh;
            generatedKinds[mesh] = kind;
            Log($"Mesh '{original.name}' ({AssetDatabase.GetAssetPath(original)}) -> {name} [{kind}]");
            return mesh;
        }

        static Material[] MaterialsFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.CarBody: return new[] { materials["CarPlayer"] };
                case Kind.Wheel: return new[] { materials["Tyre"] };
                case Kind.Pawn: return new[] { materials["Unit"] };
                case Kind.House: return new[] { materials["Building"] };
                case Kind.Tree: return new[] { materials["Trunk"], materials["Foliage"] };
                case Kind.Stump: return new[] { materials["Trunk"] };
                case Kind.Rock: return new[] { materials["Rock"] };
                case Kind.Ring: return new[] { materials["Ring"] };
                default: return new[] { materials["Prop"] };
            }
        }

        /// <summary>Unpacks instances of third-party prefabs so their objects can be edited.</summary>
        static int UnpackThirdPartyInstances(IEnumerable<GameObject> roots)
        {
            int count = 0;
            bool changed = true;
            // Unpacking can expose nested instances, so repeat until none are left.
            while (changed)
            {
                changed = false;
                foreach (GameObject root in roots)
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                            continue;
                        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                        if (!IsThirdParty(source))
                            continue;
                        // A nested instance inside one of our own prefabs is fixed in that prefab instead.
                        if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go) && !IsThirdParty(PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetOutermostPrefabInstanceRoot(go))))
                            continue;
                        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                        count++;
                        changed = true;
                    }
                }
            }
            return count;
        }

        /// <summary>Swaps third-party meshes, collision meshes and materials for generated ones.</summary>
        static int ReplaceInHierarchy(GameObject root)
        {
            int count = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsThirdParty(filter.sharedMesh))
                    continue;
                Mesh mesh = ReplacementFor(filter.sharedMesh);
                filter.sharedMesh = mesh;
                if (filter.TryGetComponent(out MeshRenderer renderer))
                    AssignMaterials(renderer, generatedKinds[mesh]);
                count++;
            }

            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!IsThirdParty(collider.sharedMesh))
                    continue;
                collider.sharedMesh = ReplacementFor(collider.sharedMesh);
                count++;
            }

            // Any third-party material left on a renderer whose mesh was not third-party.
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] current = renderer.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (IsThirdParty(current[i]))
                    {
                        current[i] = materials["Prop"];
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    renderer.sharedMaterials = current;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Puts the kind's materials in the first slots and keeps any extra slot that is ours
        /// (for example the RTS selection outline in slot 1).
        /// </summary>
        static void AssignMaterials(Renderer renderer, Kind kind)
        {
            Material[] kindMaterials = MaterialsFor(kind);
            var result = new List<Material>(kindMaterials);
            Material[] current = renderer.sharedMaterials;
            for (int i = 1; i < current.Length; i++)
            {
                if (current[i] != null && !IsThirdParty(current[i]) && !result.Contains(current[i]))
                    result.Add(current[i]);
            }
            renderer.sharedMaterials = result.ToArray();
        }

        static int RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            return removed;
        }

        static void MigratePrefabs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int unpacked = UnpackThirdPartyInstances(new[] { root });
                    int replaced = ReplaceInHierarchy(root);
                    int missing = RemoveMissingScripts(root);

                    if (path == RemoteCarPrefab)
                        PaintCar(root, materials["CarRemote"]);
                    if (root.GetComponent<ArmyBase>() != null || root.GetComponent<ResourceNode>() != null)
                        AddCarvingObstacle(root);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Log($"Prefab {path}: unpacked {unpacked}, replaced {replaced}, removed {missing} missing scripts.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
        }

        static void AddCarvingObstacle(GameObject root)
        {
            if (root.GetComponent<NavMeshObstacle>() != null)
                return;
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            if (root.TryGetComponent(out SphereCollider sphere))
            {
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.center = sphere.center;
                obstacle.radius = sphere.radius * 0.8f;
                obstacle.height = 2f;
                return;
            }

            // Fit a box to the building's meshes, in the root's local space.
            Bounds? bounds = null;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 min = toRoot.MultiplyPoint3x4(local.min), max = toRoot.MultiplyPoint3x4(local.max);
                var b = new Bounds((min + max) * 0.5f, Vector3.zero);
                b.Encapsulate(min);
                b.Encapsulate(max);
                if (bounds.HasValue)
                    b.Encapsulate(bounds.Value);
                bounds = b;
            }
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = bounds?.center ?? Vector3.zero;
            obstacle.size = bounds?.size ?? Vector3.one;
        }

        // ------------------------------------------------------------------ racing

        static void MigrateSceneCommon(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            int unpacked = UnpackThirdPartyInstances(roots);
            // The sky dome model is replaced by the default procedural skybox.
            int removedSky = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Reverse())
                {
                    if (t != null && t.name.Contains("SimpleSky"))
                    {
                        Object.DestroyImmediate(t.gameObject);
                        removedSky++;
                    }
                }
            }
            int replaced = 0, missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                replaced += ReplaceInHierarchy(root);
                missing += RemoveMissingScripts(root);
            }

            int renamed = 0;
            var names = new Dictionary<string, string>
            {
                { "Velocimetro_TMP", "Speedometer_TMP" },
                { "Car_IA_1", "Car_AI_1" },
                { "Car_IA_2", "Car_AI_2" },
                { "Car_IA_3", "Car_AI_3" },
                { "------------- ENVIROMENT", "------------- ENVIRONMENT" },
                { "----------- ENVIROMENT", "----------- ENVIRONMENT" },
                { "DistanceMatrix", "VisionSystem" },
            };
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (names.TryGetValue(t.name.Trim(), out string english))
                    {
                        t.name = english;
                        renamed++;
                    }
                }
            }

            Log($"Scene {scene.path}: unpacked {unpacked}, replaced {replaced}, removed {missing} missing scripts, renamed {renamed}, removed {removedSky} sky domes.");
        }

        static void PaintCar(GameObject car, Material material)
        {
            foreach (MeshRenderer renderer in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && generatedKinds.TryGetValue(filter.sharedMesh, out Kind kind) && kind == Kind.CarBody)
                {
                    Material[] slots = renderer.sharedMaterials;
                    slots[0] = material;
                    renderer.sharedMaterials = slots;
                }
            }
        }

        static void MigrateSinglePlayerScene()
        {
            Scene scene = EditorSceneManager.OpenScene(SinglePlayerScene, OpenSceneMode.Single);
            MigrateSceneCommon(scene);

            // Each car gets its own colour: red for the player, blue/green/yellow for the AI.
            string[] aiColours = { "CarAI1", "CarAI2", "CarAI3" };
            int ai = 0;
            foreach (CarController car in Object.FindObjectsByType<CarController>(FindObjectsInactive.Include).OrderBy(c => c.name))
            {
                bool player = car.GetComponent<PlayerCarInput>() != null;
                PaintCar(car.gameObject, materials[player ? "CarPlayer" : aiColours[ai++ % aiColours.Length]]);
            }

            // The grid positions existed in the scene but were never assigned in single player.
            CircuitManager circuit = Object.FindAnyObjectByType<CircuitManager>(FindObjectsInactive.Include);
            GameObject grid = FindInScene(scene, "Car_SpawnPoints");
            if (circuit != null && grid != null)
            {
                var serialized = new SerializedObject(circuit);
                SerializedProperty list = serialized.FindProperty("spawnpoints");
                list.arraySize = grid.transform.childCount;
                for (int i = 0; i < grid.transform.childCount; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = grid.transform.GetChild(i);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Log($"Assigned {grid.transform.childCount} spawn points to the circuit.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void CreateOnlineScene()
        {
            AssetDatabase.DeleteAsset(OnlineScene);
            if (!AssetDatabase.CopyAsset(SinglePlayerScene, OnlineScene))
                throw new InvalidOperationException("Could not copy the single-player scene.");

            Scene scene = EditorSceneManager.OpenScene(OnlineScene, OpenSceneMode.Single);

            // Online races have only human players.
            int removed = 0;
            foreach (AICarDriver driver in Object.FindObjectsByType<AICarDriver>(FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(driver.gameObject);
                removed++;
            }

            Competitor player = Object.FindObjectsByType<Competitor>(FindObjectsInactive.Include)
                .First(c => c.GetComponent<PlayerCarInput>() != null);
            CircuitManager circuit = Object.FindAnyObjectByType<CircuitManager>(FindObjectsInactive.Include);
            var remotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RemoteCarPrefab).GetComponent<RemoteCar>();

            var manager = new GameObject("Manager_Online");
            GameObject managers = FindInScene(scene, "------------- MANAGERS");
            if (managers != null)
                manager.transform.SetParent(managers.transform, false);
            var online = manager.AddComponent<OnlineRaceManager>();
            var serialized = new SerializedObject(online);
            serialized.FindProperty("circuit").objectReferenceValue = circuit;
            serialized.FindProperty("localCompetitor").objectReferenceValue = player;
            serialized.FindProperty("remoteCarPrefab").objectReferenceValue = remotePrefab;
            serialized.FindProperty("competitorsRoot").objectReferenceValue = player.transform.parent;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log($"Created {OnlineScene}: removed {removed} AI cars, added the online race manager.");
        }

        // ------------------------------------------------------------------ RTS

        static void MigrateBattlefield()
        {
            Scene scene = EditorSceneManager.OpenScene(BattlefieldScene, OpenSceneMode.Single);
            MigrateSceneCommon(scene);

            // Army A is the player's; army B is played by the AI commander.
            foreach (ArmyController army in Object.FindObjectsByType<ArmyController>(FindObjectsInactive.Include))
            {
                var serialized = new SerializedObject(army);
                serialized.FindProperty("playerControlled").boolValue = army.name.EndsWith("A", StringComparison.Ordinal);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (Object.FindAnyObjectByType<RtsHud>(FindObjectsInactive.Include) == null)
            {
                var hud = new GameObject("HUD", typeof(RtsHud));
                GameObject canvasGroup = FindInScene(scene, "----------- CANVAS");
                if (canvasGroup != null)
                    hud.transform.SetParent(canvasGroup.transform, false);
            }

            BakeNavMesh(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Rebakes the walkable area with the AI Navigation package (the old bake used the removed models).</summary>
        static void BakeNavMesh(Scene scene)
        {
            string folder = Path.ChangeExtension(BattlefieldScene, null);
            foreach (string guid in AssetDatabase.FindAssets("t:NavMeshData", new[] { folder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

            NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                var go = new GameObject("NavMesh Surface");
                GameObject environment = FindInScene(scene, "----------- ENVIRONMENT");
                if (environment != null)
                    go.transform.SetParent(environment.transform, false);
                surface = go.AddComponent<NavMeshSurface>();
            }
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = LayerMask.GetMask("Floor", "Default");
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
                throw new InvalidOperationException("NavMesh bake produced no data.");
            surface.navMeshData.name = "Battlefield NavMesh";
            AssetDatabase.CreateAsset(surface.navMeshData, folder + "/NavMesh.asset");
            EditorUtility.SetDirty(surface);
            Log($"Baked the RTS NavMesh ({surface.navMeshData.sourceBounds.size}).");
        }

        // ------------------------------------------------------------------ menu and build

        static void CreateMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UiTheme.Background;
            new GameObject("Main Menu", typeof(MainMenu));
            EditorSceneManager.SaveScene(scene, MenuScene);
            Log("Created " + MenuScene);
        }

        static void RegisterBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(SinglePlayerScene, true),
                new EditorBuildSettingsScene(OnlineScene, true),
                new EditorBuildSettingsScene(BattlefieldScene, true),
            };
            Log("Build scenes: MainMenu, SinglePlayer, Online, Battlefield.");
        }

        static void DeleteObsoleteAssets()
        {
            foreach (string path in new[]
            {
                "Assets/_Project/Racing/Scenes/MainMenu.unity",
                "Assets/_Project/Racing/Scenes/MultiPlayer.unity",
                ThirdPartyRoot,
            })
            {
                if (AssetDatabase.DeleteAsset(path))
                    Log("Deleted " + path);
            }
        }

        static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.Trim() == name)
                        return t.gameObject;
                }
            }
            return null;
        }

        static void Log(string line)
        {
            report.Add(line);
            Debug.Log("[Migration] " + line);
        }
    }
}
