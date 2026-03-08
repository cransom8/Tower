// LVESceneSetup.cs — Builds the volcanic lava-lake environment for Game_ML and Game_Survival.
//
// Menu: Castle Defender → Setup → Build LVE Background
//       Castle Defender → Setup → Build LVE Background (Both Scenes)
//       Castle Defender → Setup → Setup LVE Lighting
//       Castle Defender → Setup → Setup LVE Lighting (Both Scenes)
//
// LVE shaders ARE URP Shader Graph ("RenderPipeline"="UniversalPipeline") — no conversion needed.
// The cinematic look comes from post-processing + skybox, applied by "Setup LVE Lighting".
//
// Creates a "Background" GameObject in the active scene containing:
//   • Lava floor  — large emissive orange quad at Y = -3
//   • Bridge decks — dark stone cube underside per lane (4 total)
//   • Cliff walls  — LVE lava_wall prefabs at left/right ends (X ≈ ±30)
//   • Center rocks — LVE rock prefabs scattered in the Z-gap between bridge pairs
//   • Lava particles — LVE smoke/ejection particles at cliff bases
//
// If LVE prefabs cannot be found, coloured Unity primitive proxies are used instead.
//
// Battlefield geometry reference (from TileGrid.cs BranchConfigs):
//   Lane 0 Red  : origin (0,0,13)  colDir (0,0,-1) rowDir (-1,0,0)  spans X[0..-27] Z[3..13]
//   Lane 1 Gold : origin (0,0,-3)  colDir (0,0,-1) rowDir (-1,0,0)  spans X[0..-27] Z[-13..-3]
//   Lane 2 Blue : origin (0,0,3)   colDir (0,0, 1) rowDir ( 1,0,0)  spans X[0..+27] Z[3..13]
//   Lane 3 Green: origin (0,0,-13) colDir (0,0, 1) rowDir ( 1,0,0)  spans X[0..+27] Z[-13..-3]
//   Center channel: Z [-3..+3], full X width — lava/rock fill
//   Castle ends: X ≈ ±28, cliff walls here

#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering;

namespace CastleDefender.Editor
{
    public static class LVESceneSetup
    {
        // ── LVE asset paths — sourced from EditorPaths.cs ────────────────────
        // Do not redeclare these here. All path constants live in EditorPaths.
        const string LVE_ROOT   = EditorPaths.LVE_ROOT;       // kept as local alias for brevity
        const string ROCKS_STD  = EditorPaths.LVE_ROCKS;
        const string CLIFFS_V2  = EditorPaths.LVE_CLIFFS;
        const string PARTICLES  = EditorPaths.LVE_PARTICLES;

        // Bridge geometry constants (must match TileGrid BranchConfigs)
        const float BRIDGE_HALF_LEN = 13.5f;   // half of 27 tiles (rows 0-27)
        const float BRIDGE_WIDTH    = 11f;      // 11 tiles wide (cols 0-10)
        const float BRIDGE_CENTER_Z = 8f;       // upper pair center Z
        const float DECK_Y          = -0.55f;   // just below tile floor at Y=0
        const float DECK_THICKNESS  = 1f;

        const float CLIFF_X         = 29f;      // castle-side cliff X offset
        const float CLIFF_HALF_SPAN = 13f;      // Z half-span of cliffs
        const float LAVA_Y          = -3f;      // lava floor elevation
        const float CENTER_Z_HALF   = 3f;       // half-width of center Z channel

        // ── Menu items ────────────────────────────────────────────────────────

        [MenuItem("Castle Defender/Setup/Build LVE Background")]
        static void BuildCurrentScene()
        {
            Build();
            Debug.Log("[LVESetup] Done. Run 'Setup LVE Lighting' for bloom + skybox.");
        }

        [MenuItem("Castle Defender/Setup/Build LVE Background (Both Scenes)")]
        static void BuildBothScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var sceneML = EditorSceneManager.OpenScene(EditorPaths.SCENE_ML, OpenSceneMode.Single);
            Build();
            EditorSceneManager.SaveScene(sceneML);

            var sceneSurv = EditorSceneManager.OpenScene(EditorPaths.SCENE_SURVIVAL, OpenSceneMode.Single);
            Build();
            EditorSceneManager.SaveScene(sceneSurv);

            Debug.Log("[LVESetup] Both scenes built and saved.");
        }

        [MenuItem("Castle Defender/Setup/Setup LVE Lighting")]
        static void SetupLightingCurrentScene()
        {
            SetupLighting();
            Debug.Log("[LVESetup] Lighting set up. Check scene for Global Volume and Directional Light.");
        }

        [MenuItem("Castle Defender/Setup/Setup LVE Lighting (Both Scenes)")]
        static void SetupLightingBothScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var sceneML = EditorSceneManager.OpenScene(EditorPaths.SCENE_ML, OpenSceneMode.Single);
            SetupLighting();
            EditorSceneManager.SaveScene(sceneML);

            var sceneSurv = EditorSceneManager.OpenScene(EditorPaths.SCENE_SURVIVAL, OpenSceneMode.Single);
            SetupLighting();
            EditorSceneManager.SaveScene(sceneSurv);

            Debug.Log("[LVESetup] Lighting applied to both scenes.");
        }

        // ── Lighting / post-processing setup ─────────────────────────────────
        // Applies the LVE demo scene's look: lava sky, warm directional light,
        // and the PostProcessVolumeProfile Lava (Bloom + color grading).

        static void SetupLighting()
        {
            // ── Skybox ────────────────────────────────────────────────────────
            // Try the lava sky first, fall back to the dark skybox
            var sky = AssetDatabase.LoadAssetAtPath<Material>(EditorPaths.LVE_SKYBOX);
            if (sky == null)
                sky = AssetDatabase.LoadAssetAtPath<Material>(EditorPaths.LVE_SKYBOX_ALT);
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                Debug.Log("[LVESetup] Skybox set to: " + sky.name);
            }
            else
            {
                Debug.LogWarning("[LVESetup] Lava skybox not found — skipping.");
            }

            // ── Ambient light — dark reddish to match lava cave ───────────────
            RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.12f, 0.05f, 0.03f);
            RenderSettings.fogColor     = new Color(0.10f, 0.04f, 0.02f);
            RenderSettings.fog          = true;
            RenderSettings.fogMode      = FogMode.Linear;
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance   = 60f;

            // ── Directional light — warm orange-red lava glow ─────────────────
            var dirLight = Object.FindFirstObjectByType<Light>();
            if (dirLight != null && dirLight.type == LightType.Directional)
            {
                dirLight.color     = new Color(1.0f, 0.45f, 0.15f);
                dirLight.intensity = 1.2f;
                dirLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                EditorUtility.SetDirty(dirLight);
                Debug.Log("[LVESetup] Directional light set to warm lava orange.");
            }
            else
            {
                Debug.LogWarning("[LVESetup] No Directional Light found in scene — add one manually.");
            }

            // ── Global Volume (post-processing) ───────────────────────────────
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(EditorPaths.LVE_POSTPROCESS);

            if (profile != null)
            {
                // Find or create a Global Volume GO
                var vol = Object.FindFirstObjectByType<Volume>();
                if (vol == null)
                {
                    var volGo = new GameObject("GlobalVolume_LVE");
                    vol = volGo.AddComponent<Volume>();
                }
                vol.isGlobal        = true;
                vol.priority        = 10f;
                vol.sharedProfile   = profile;
                EditorUtility.SetDirty(vol);
                Debug.Log("[LVESetup] Global Volume set to PostProcessVolumeProfile Lava.");
            }
            else
            {
                Debug.LogWarning("[LVESetup] PostProcessVolumeProfile Lava.asset not found. " +
                                 "Bloom will not be applied. Check path: " + EditorPaths.LVE_POSTPROCESS);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ── Main builder ──────────────────────────────────────────────────────

        static void Build()
        {
            // Remove any existing Background so we rebuild clean
            var existing = GameObject.Find("Background");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                Debug.Log("[LVESetup] Removed old Background.");
            }

            var root = new GameObject("Background");

            AddLavaFloor(root);
            AddBridgeDecks(root);
            AddBridgeRailings(root);
            AddCliffWalls(root);
            AddCenterRocks(root);
            AddLavaParticles(root);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[LVESetup] Background built in scene '" +
                      EditorSceneManager.GetActiveScene().name + "'.");
        }

        // ── Lava floor ────────────────────────────────────────────────────────

        static void AddLavaFloor(GameObject root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Lava_Floor";
            go.transform.parent = root.transform;
            go.transform.position = new Vector3(0f, LAVA_Y, 0f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(70f, 40f, 1f);

            Object.DestroyImmediate(go.GetComponent<MeshCollider>());

            var mat = MakeUrpMaterial("Lava_Floor_Mat",
                baseColor: new Color(0.9f, 0.25f, 0.05f),
                emissiveColor: new Color(1.2f, 0.3f, 0f),
                metallic: 0f, smoothness: 0.6f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ── Bridge deck bases ─────────────────────────────────────────────────
        // One dark-stone cube per lane sitting just below the tile floor.

        static void AddBridgeDecks(GameObject root)
        {
            // (centerX, centerZ)
            var centers = new (float x, float z, string name)[]
            {
                (-BRIDGE_HALF_LEN,  BRIDGE_CENTER_Z, "BridgeDeck_Lane0_Red"),
                (-BRIDGE_HALF_LEN, -BRIDGE_CENTER_Z, "BridgeDeck_Lane1_Gold"),
                ( BRIDGE_HALF_LEN,  BRIDGE_CENTER_Z, "BridgeDeck_Lane2_Blue"),
                ( BRIDGE_HALF_LEN, -BRIDGE_CENTER_Z, "BridgeDeck_Lane3_Green"),
            };

            // Prefer an LVE dark volcanic ground material for authentic cracked-rock look.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(EditorPaths.LVE_GROUND_MAT);
            if (mat == null)
                mat = MakeUrpMaterial("BridgeDeck_Mat",
                    baseColor: new Color(0.22f, 0.18f, 0.16f),
                    emissiveColor: new Color(0.15f, 0.04f, 0f),
                    metallic: 0.1f, smoothness: 0.15f);

            foreach (var (cx, cz, label) in centers)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = label;
                go.transform.parent = root.transform;
                go.transform.position = new Vector3(cx, DECK_Y - DECK_THICKNESS * 0.5f, cz);
                go.transform.localScale = new Vector3(27f, DECK_THICKNESS, BRIDGE_WIDTH);
                Object.DestroyImmediate(go.GetComponent<BoxCollider>());
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // ── Bridge railings ───────────────────────────────────────────────────
        // Stone pillar posts + hanging chain spans along the long sides of each bridge.
        // Concept: dark stone posts every ~4.5u, thin cylinders sag between them like chains.

        static readonly (float cx, float cz, string tag)[] BridgeDefs =
        {
            (-13.5f,  8f, "Lane0_Red"),
            (-13.5f, -8f, "Lane1_Gold"),
            ( 13.5f,  8f, "Lane2_Blue"),
            ( 13.5f, -8f, "Lane3_Green"),
        };

        static void AddBridgeRailings(GameObject root)
        {
            var group = new GameObject("Bridge_Railings");
            group.transform.parent = root.transform;

            var postMat  = MakeUrpMaterial("BridgePost_Mat",
                new Color(0.20f, 0.15f, 0.13f), new Color(0.10f, 0.03f, 0f), 0.1f, 0.1f);
            var chainMat = MakeUrpMaterial("BridgeChain_Mat",
                new Color(0.18f, 0.13f, 0.10f), new Color(0.25f, 0.06f, 0f), 0.5f, 0.35f);

            float halfLen   = 13.0f;   // slightly inside bridge ends
            float halfWidth = 5.0f;    // inside edge of bridge width
            float postH     = 1.4f;    // post height above tile floor

            // X positions of posts along the bridge (4 per side)
            float[] xOffsets = { -halfLen, -halfLen * 0.33f, halfLen * 0.33f, halfLen };

            foreach (var (cx, cz, tag) in BridgeDefs)
            {
                var bg = new GameObject("Rails_" + tag);
                bg.transform.parent = group.transform;

                foreach (float zSign in new[] { -1f, 1f })
                {
                    float zSide = cz + zSign * halfWidth;

                    // Posts
                    for (int i = 0; i < xOffsets.Length; i++)
                    {
                        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        post.name = $"Post_{tag}_{i}";
                        post.transform.parent = bg.transform;
                        post.transform.position = new Vector3(cx + xOffsets[i], postH * 0.5f, zSide);
                        post.transform.localScale = new Vector3(0.30f, postH, 0.30f);
                        Object.DestroyImmediate(post.GetComponent<BoxCollider>());
                        post.GetComponent<Renderer>().sharedMaterial = postMat;
                    }

                    // Sagging chain spans between consecutive posts
                    for (int i = 0; i < xOffsets.Length - 1; i++)
                        AddChainSpan(bg, chainMat,
                            cx + xOffsets[i], cx + xOffsets[i + 1],
                            zSide, postH);
                }
            }
        }

        static void AddChainSpan(GameObject parent, Material mat,
                                  float x0, float x1, float z, float postH)
        {
            float midX = (x0 + x1) * 0.5f;
            float sagY = postH * 0.55f;   // droops to 55% of post height
            float topY = postH * 0.82f;   // attaches near post top

            AddChainSeg(parent, mat, new Vector3(x0, topY, z), new Vector3(midX, sagY, z));
            AddChainSeg(parent, mat, new Vector3(midX, sagY, z), new Vector3(x1, topY, z));
        }

        static void AddChainSeg(GameObject parent, Material mat, Vector3 from, Vector3 to)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Chain";
            go.transform.parent = parent.transform;
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;

            Vector3 dir = to - from;
            float len = dir.magnitude;
            go.transform.position = (from + to) * 0.5f;
            go.transform.localScale = new Vector3(0.06f, len * 0.5f, 0.06f);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        }

        // ── Cliff walls ───────────────────────────────────────────────────────
        // LVE lava_wall prefabs stacked at left (X=-29) and right (X=+29) ends.

        static void AddCliffWalls(GameObject root)
        {
            // Try V2 prefabs first (better meshes); fall back to plain cube proxies
            string[] wallPaths =
            {
                CLIFFS_V2 + "/lava_wall_01_V2.prefab",
                CLIFFS_V2 + "/lava_wall_03_V2.prefab",
                CLIFFS_V2 + "/lava_wall_05_V2.prefab",
                CLIFFS_V2 + "/lava_wall_07_V2.prefab",
            };

            PlaceCliffSide(root, -CLIFF_X, "Cliff_Left",  wallPaths);
            PlaceCliffSide(root,  CLIFF_X, "Cliff_Right", wallPaths);
        }

        static void PlaceCliffSide(GameObject root, float posX, string groupName, string[] wallPaths)
        {
            var group = new GameObject(groupName);
            group.transform.parent = root.transform;

            // Z positions for cliff segments spanning -13 to +13
            float[] zOffsets = { -9f, -3f, 3f, 9f };
            float yaw = posX < 0 ? 90f : -90f;

            for (int i = 0; i < zOffsets.Length; i++)
            {
                string path  = i < wallPaths.Length ? wallPaths[i] : wallPaths[0];
                var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                GameObject go;

                if (prefab != null)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    URPSafe(go, new Color(0.18f, 0.12f, 0.10f), new Color(0.4f, 0.1f, 0f));
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.GetComponent<Renderer>().sharedMaterial =
                        MakeUrpMaterial("CliffProxy_Mat", new Color(0.18f, 0.12f, 0.10f),
                                        new Color(0.3f, 0.08f, 0f), 0.05f, 0.1f);
                    Object.DestroyImmediate(go.GetComponent<BoxCollider>());
                    go.transform.localScale = new Vector3(4f, 8f, 6f);
                }

                go.name = $"{groupName}_Seg{i}";
                go.transform.parent = group.transform;
                go.transform.position = new Vector3(posX, 0f, zOffsets[i]);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * 1.8f;
            }
        }

        // ── Center channel rocks ──────────────────────────────────────────────
        // LVE rock prefabs scattered in the Z = [-3..+3] gap between bridge pairs,
        // and along the bridge edges where the lane pairs meet the center.

        static void AddCenterRocks(GameObject root)
        {
            var group = new GameObject("Center_Rocks");
            group.transform.parent = root.transform;

            string[] rockPaths =
            {
                ROCKS_STD + "/prefab_A_big_lava_rock_01.prefab",
                ROCKS_STD + "/prefab_B_big_lava_rock_01.prefab",
                ROCKS_STD + "/prefab_A_medium_lava_rock_01.prefab",
                ROCKS_STD + "/prefab_B_medium_lava_rock_03.prefab",
                ROCKS_STD + "/prefab_A_small_lava_rock_01.prefab",
                ROCKS_STD + "/prefab_B_small_lava_rock_05.prefab",
            };

            // Center channel positions (Z -3 to +3, scattered along X)
            var positions = new Vector3[]
            {
                new(-20f, LAVA_Y + 0.3f, -1.5f),
                new(-12f, LAVA_Y + 0.5f,  2f),
                new( -5f, LAVA_Y + 0.2f, -2f),
                new(  0f, LAVA_Y + 0.6f,  0f),
                new(  6f, LAVA_Y + 0.3f,  1.5f),
                new( 13f, LAVA_Y + 0.4f, -1f),
                new( 21f, LAVA_Y + 0.5f,  2f),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                string path   = rockPaths[i % rockPaths.Length];
                var    prefab  = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                GameObject go;

                if (prefab != null)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    URPSafe(go, new Color(0.15f, 0.10f, 0.08f), new Color(0.6f, 0.15f, 0f));
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.GetComponent<Renderer>().sharedMaterial =
                        MakeUrpMaterial("RockProxy_Mat", new Color(0.15f, 0.10f, 0.08f),
                                        new Color(0.5f, 0.12f, 0f), 0.05f, 0.1f);
                    Object.DestroyImmediate(go.GetComponent<SphereCollider>());
                    float sz = 1f + (i % 3) * 0.4f;
                    go.transform.localScale = new Vector3(sz, sz * 0.7f, sz);
                }

                go.name = $"Rock_{i}";
                go.transform.parent    = group.transform;
                go.transform.position  = positions[i];
                go.transform.rotation  = Quaternion.Euler(0f, i * 47f, 0f);
            }
        }

        // ── Lava particles ────────────────────────────────────────────────────

        static void AddLavaParticles(GameObject root)
        {
            var group = new GameObject("Lava_Particles");
            group.transform.parent = root.transform;

            string smokePath = PARTICLES + "/Lava Smoke.prefab";
            string ejPath    = PARTICLES + "/Lava Ejaculation Small.prefab";

            // Smoke columns near cliff bases and center
            var smokePositions = new Vector3[]
            {
                new(-27f, LAVA_Y + 1f,  5f),
                new(-27f, LAVA_Y + 1f, -5f),
                new(  0f, LAVA_Y + 1f,  0f),
                new( 27f, LAVA_Y + 1f,  5f),
                new( 27f, LAVA_Y + 1f, -5f),
            };

            // Ejection bursts near center and cliff edges
            var ejPositions = new Vector3[]
            {
                new(-15f, LAVA_Y + 0.5f, 0f),
                new(  8f, LAVA_Y + 0.5f, 1f),
            };

            PlaceParticles(group, smokePath, "Smoke", smokePositions);
            PlaceParticles(group, ejPath,    "Eject", ejPositions);
        }

        static void PlaceParticles(GameObject parent, string path, string prefix, Vector3[] positions)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[LVESetup] Particle prefab not found: {path}");
                return;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                go.name = $"{prefix}_{i}";
                go.transform.position = positions[i];
            }
        }

        // ── URP material helpers ──────────────────────────────────────────────

        static Material MakeUrpMaterial(string name, Color baseColor, Color emissiveColor,
                                        float metallic, float smoothness)
        {
            string dir  = "Assets/Materials/LVE";
            string path = $"{dir}/{name}.mat";

            // Reuse existing to avoid duplicates on rebuild
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "LVE");
                AssetDatabase.Refresh();
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            mat.name = name;

            mat.SetColor("_BaseColor",  baseColor);
            mat.SetColor("_Color",      baseColor);  // Standard fallback

            if (emissiveColor != Color.black)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissiveColor);
            }

            mat.SetFloat("_Metallic",   metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Glossiness", smoothness); // Standard fallback

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // Applies URP-safe colour overrides to all Renderers in a prefab instance.
        // Preserves the existing material instance (doesn't create a new asset).
        static void URPSafe(GameObject go, Color baseColor, Color emissiveColor)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    // Only swap if on a Built-in / missing shader.
                    // Skip any shader that is already URP-family or NatureManufacture Shader Graph.
                    string sName = mats[i].shader != null ? mats[i].shader.name : "";
                    if (sName.Contains("Universal") || sName.Contains("NatureManufacture") ||
                        sName.Contains("Shader Graphs")) continue;

                    var m = new Material(shader);
                    m.SetColor("_BaseColor",     baseColor);
                    m.SetColor("_EmissionColor", emissiveColor);
                    if (emissiveColor != Color.black) m.EnableKeyword("_EMISSION");
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }
    }
}
#endif
