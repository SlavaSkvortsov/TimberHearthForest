using Epic.OnlineServices;
using HarmonyLib;
using OWML.Common;
using OWML.Logging;
using OWML.ModHelper;
using OWML.Utils;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TimberHearthForest
{
    public class TimberHearthForest : ModBehaviour
    {
        public static TimberHearthForest Instance;

        private const int SECTOR_SIZE = 100;

        private const float MAX_GRASS_DISTANCE = 200.0f;
        private const float MAX_FIREFLY_DISTANCE = 100.0f;

        private Dictionary<Vector3Int, GameObject> propSectors = new Dictionary<Vector3Int, GameObject>();

        private List<GameObject> spawnedTrees = new List<GameObject>();
        private List<GameObject> spawnedGrass = new List<GameObject>();

        private List<ParticleSystem> spawnedFireflies = new List<ParticleSystem>();

        private Sector timberHearthSector;
        private Sector quantumMoonSector;

        private bool firefliesEnabledAtNight = true;
        private bool firefliesEnabledAtDay = true;

        private List<string> assetBundles = new List<string>();

        public class PropDetails
        {
            public Vector3 rotation;
            public Vector3 position;
        }

        public void Awake()
        {
            Instance = this;
            // You won't be able to access OWML's mod helper in Awake.
            // So you probably don't want to do anything here.
            // Use Start() instead.
        }

        public void Start()
        {
            // Starting here, you'll have access to OWML's mod helper.
            ModHelper.Console.WriteLine($"{nameof(TimberHearthForest)} is loaded!", MessageType.Success);

            new Harmony("GameDev46.TimberHearthForest").PatchAll(Assembly.GetExecutingAssembly());

            // Example of accessing game code.
            OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen); // We start on title screen
            LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        }

        public void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene)
        {
            if (newScene != OWScene.SolarSystem) return;
            //ModHelper.Console.WriteLine("Loaded into solar system!", MessageType.Success);

            // Loaded into Solar System!

            // Load tree data and spawn trees
            string treeSpawnDataPath = ModHelper.Manifest.ModFolderPath + "Assets/treeSpawnData.json";
            LoadAndSpawnProps(treeSpawnDataPath);
        }

        /// Called by OWML; once at the start and upon each config setting change.
        public override void Configure(IModConfig config)
        {
            string treeDensityPreset = config.GetSettingsValue<string>("treeDensity");
            UpdatePropDensity(treeDensityPreset, "tree");

            bool treeCollidersEnabled = config.GetSettingsValue<string>("treeCollidersEnabled") == "Enabled";
            ToggleTreeHitboxes(treeCollidersEnabled);

            string grassDensityPreset = config.GetSettingsValue<string>("grassDensity");
            UpdatePropDensity(grassDensityPreset, "grass");

            string fireflyEnabledState = config.GetSettingsValue<string>("fireflyEnabled");
            firefliesEnabledAtNight = fireflyEnabledState == "Night" || fireflyEnabledState == "Day and Night";
            firefliesEnabledAtDay = fireflyEnabledState == "Day" || fireflyEnabledState == "Day and Night";

            string fireflyDensityPreset = config.GetSettingsValue<string>("fireflyDensity");
            UpdatePropDensity(fireflyDensityPreset, "firefly");
        }

        private void LoadAndSpawnProps(string jsonFilePath)
        {

            if (!System.IO.File.Exists(jsonFilePath))
            {
                ModHelper.Console.WriteLine($"Couldn't find {jsonFilePath}", MessageType.Error);
                return;
            }

            string json = System.IO.File.ReadAllText(jsonFilePath);
            List<PropDetails> spawnData = ParseJson(json);

            if (spawnData == null)
            {
                ModHelper.Console.WriteLine("Loaded data is null.", MessageType.Error);
                return;
            }

            StartCoroutine(SpawnTrees(spawnData));
        }

        IEnumerator SpawnTrees(List<PropDetails> treeData)
        {
            propSectors = new Dictionary<Vector3Int, GameObject>();

            // Clear the stored trees, grass tufts and particle systems
            spawnedTrees = new List<GameObject>();
            spawnedGrass = new List<GameObject>();

            spawnedFireflies = new List<ParticleSystem>();

            // Wait for scene to load
            yield return new WaitForSeconds(3f);

            // Locate TimberHearth_Body
            GameObject timberHearthBody = GameObject.Find("TimberHearth_Body");

            if (timberHearthBody == null)
            {
                ModHelper.Console.WriteLine("Couldn't locate the TimberHearth_Body gameobject", MessageType.Error);
                yield break;
            }

            ModHelper.Console.WriteLine("Located TimberHearth_Body object successfully", MessageType.Success);

            // Locate the tree template gameobject
            const string treeTemplatePath = "QuantumMoon_Body/Sector_QuantumMoon/State_TH/Interactables_THState/Crater_Surface/Surface_AlpineTrees_Single/QAlpine_Tree_.25 (1)";
            GameObject treeTemplate = GetGameObjectAtPath(treeTemplatePath);

            if (treeTemplate == null)
            {
                ModHelper.Console.WriteLine("Couldn't locate the tree template gameobject at: " + treeTemplatePath, MessageType.Error);
                yield break;
            }

            // Load the tree template's asset bundle to prevent the clones from being invisible
            foreach (var handle in treeTemplate.GetComponentsInChildren<StreamingMeshHandle>(true))
            {
                if (!string.IsNullOrEmpty(handle.assetBundle)) assetBundles.Add(handle.assetBundle);
            }

            // Locate the grass template gameobject
            const string grassTemplatePath = "TimberHearth_Body/Sector_TH/Sector_Village/Sector_LowerVillage/DetailPatches_LowerVillage/LandingGeyserVillageArea/Foliage_TH_GrassPatch (10)";
            GameObject grassTemplate = GetGameObjectAtPath(grassTemplatePath);

            if (grassTemplate == null)
            {
                ModHelper.Console.WriteLine("Couldn't locate the grass template gameobject at: " + grassTemplatePath, MessageType.Error);
                yield break;
            }

            // Load the grass template's asset bundle to prevent the clones from being invisible
            var grassHandle = grassTemplate.GetComponent<StreamingMeshHandle>();
            if (grassHandle) if (!string.IsNullOrEmpty(grassHandle.assetBundle)) assetBundles.Add(grassHandle.assetBundle);

            // Locate the Timber Hearth and Quantum Moon Sector
            timberHearthSector = Locator.GetAstroObject(AstroObject.Name.TimberHearth).GetComponentInChildren<Sector>();
            quantumMoonSector = Locator.GetAstroObject(AstroObject.Name.QuantumMoon).GetComponentInChildren<Sector>();

            timberHearthSector.OnOccupantEnterSector += OnEnterTimberHearth;
            quantumMoonSector.OnOccupantExitSector += OnLeaveQuantumMoon;

            // Load all the asset bundles to prevent the clones from being invisible
            foreach (string bundle in assetBundles) StreamingManager.LoadStreamingAssets(bundle);

            // Used to group sectors clones together for a cleaner hierachy
            GameObject sectorsParent = new GameObject($"TH_Forest_Sectors");
            sectorsParent.transform.SetParent(timberHearthSector.transform, false);
            sectorsParent.transform.localPosition = Vector3.zero;
            sectorsParent.transform.localRotation = Quaternion.identity;

            int index = 0;

            foreach (PropDetails detail in treeData) {
                // Get the detail's sector location
                Vector3 THLocalCoords = new Vector3(detail.position.x, detail.position.y, detail.position.z);
                Vector3Int sectorCoords = GetSectorCoordsFromTHCoords(THLocalCoords);

                Vector3 detailWorldCoords = timberHearthBody.transform.TransformPoint(THLocalCoords);

                // Check if the sector exists
                if (!propSectors.ContainsKey(sectorCoords))
                {
                    // Create the sector holder
                    GameObject sectorHolder = CreateTHSector(sectorsParent, sectorCoords);
                    propSectors[sectorCoords] = sectorHolder;
                }

                GameObject sectorParent = propSectors[sectorCoords];
                Transform treeParent = sectorParent.transform.Find("TH_Trees_Surface");
                Transform grassParent = sectorParent.transform.Find("TH_Grass_Surface");
                Transform firefliesParent = sectorParent.transform.Find("TH_Fireflies_Surface");

                // Spawn the tree
                GameObject treeClone = Instantiate(treeTemplate);

                // Parent the tree
                treeClone.transform.SetParent(treeParent, false);

                // Remove quantum components to prevent weird interactions with the tree clones
                StripQuantumComponents(treeClone);

                // Add some random rotation to make the trees look more natural
                Vector3 randOffsets = new Vector3(
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-0.5f, 0.5f)
                );

                float randomScale = UnityEngine.Random.Range(0.7f, 1.4f);

                // Set position, rotation and scale
                treeClone.transform.position = detailWorldCoords;
                treeClone.transform.localRotation = Quaternion.Euler(detail.rotation.x + randOffsets.x, detail.rotation.y + randOffsets.y, detail.rotation.z + randOffsets.z);
                treeClone.transform.localScale = new Vector3(randomScale, randomScale, randomScale);

                foreach (var tracker in treeClone.GetComponentsInChildren<ShapeVisibilityTracker>(true))
                {
                    DestroyImmediate(tracker);
                }

                // Add a collider to the trees
                CapsuleCollider coll = treeClone.AddComponent<CapsuleCollider>();
                coll.enabled = false;
                coll.radius = 0.35f;
                coll.height = 20.0f;
                coll.center = Vector3.up * 7.0f;

                spawnedTrees.Add(treeClone);

                // Check to add the firefly particle effects
                if (index % 10 == 0) AddFireflies(treeClone.transform, firefliesParent);
                index++;

                /* ------------ */
                /* GRASS TUFTS */
                /* ------------*/

                // Spawn the grass tuft
                GameObject grassClone = Instantiate(grassTemplate);

                // Parent the tree
                grassClone.transform.SetParent(grassParent, false);

                randomScale = UnityEngine.Random.Range(0.8f, 1.2f);

                // Set position, rotation and scale
                grassClone.transform.position = detailWorldCoords;
                grassClone.transform.localRotation = Quaternion.Euler(detail.rotation.x, detail.rotation.y, detail.rotation.z);
                grassClone.transform.localScale = new Vector3(randomScale, randomScale, randomScale);

                grassClone.GetComponent<Renderer>()?.enabled = true;

                spawnedGrass.Add(grassClone);
            }

            ModHelper.Console.WriteLine("All trees and grass tufts have been spawned.", MessageType.Success);

            // Update the tree and grass density
            string treeDensityPreset = ModHelper.Config.GetSettingsValue<string>("treeDensity");
            UpdatePropDensity(treeDensityPreset, "tree");

            bool treeCollidersEnabled = ModHelper.Config.GetSettingsValue<string>("treeCollidersEnabled") == "Enabled";
            ToggleTreeHitboxes(treeCollidersEnabled);

            string grassDensityPreset = ModHelper.Config.GetSettingsValue<string>("grassDensity");
            UpdatePropDensity(treeDensityPreset, "grass");

            string fireflyEnabledState = ModHelper.Config.GetSettingsValue<string>("fireflyEnabled");
            firefliesEnabledAtNight = fireflyEnabledState == "Night" || fireflyEnabledState == "Day and Night";
            firefliesEnabledAtDay = fireflyEnabledState == "Day" || fireflyEnabledState == "Day and Night";

            string fireflyDensityPreset = ModHelper.Config.GetSettingsValue<string>("fireflyDensity");
            UpdatePropDensity(fireflyDensityPreset, "firefly");
        }

        private void OnEnterTimberHearth(SectorDetector detector)
        {
            // Should only load bundles if the player is entering Timber Hearth
            if (!timberHearthSector.ContainsOccupant(DynamicOccupant.Player)) return;
            // Load all the asset bundles
            foreach (string bundle in assetBundles) StreamingManager.LoadStreamingAssets(bundle);
        }

        private void OnLeaveQuantumMoon(SectorDetector detector)
        {
            // Should only load bundles if the player is leaving the quantum moon
            if (quantumMoonSector.ContainsOccupant(DynamicOccupant.Player)) return;
            // Load all the asset bundles
            foreach (string bundle in assetBundles) StreamingManager.LoadStreamingAssets(bundle);
        }

        void AddFireflies(Transform treeTransform, Transform fireflyHolder)
        {
            GameObject fireflyObj = new GameObject("Fireflies");
            fireflyObj.transform.SetParent(fireflyHolder, false);

            fireflyObj.transform.localPosition = treeTransform.localPosition + Vector3.up * 5.0f;
            fireflyObj.transform.localRotation = Quaternion.Euler(treeTransform.localRotation.x, treeTransform.localRotation.y, treeTransform.localRotation.z);
            fireflyObj.transform.localScale = Vector3.one;

            var ps = fireflyObj.AddComponent<ParticleSystem>();

            ps.Pause();

            var main = ps.main;
            main.loop = false;
            main.startLifetime = UnityEngine.Random.Range(4f, 10f);
            main.startSpeed = UnityEngine.Random.Range(0.1f, 0.3f);
            main.startSize = UnityEngine.Random.Range(0.02f, 0.04f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.scrollSpeed = 1.0f;
            noise.octaveCount = 4;
            noise.octaveScale = 0.7f;
            noise.positionAmount = 1.0f;

            var shape = ps.shape;
            shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Random;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40.0f, 10.0f, 40.0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.y = 0.1f;

            Material fireflyMaterial = new Material(Shader.Find("Standard"));
            fireflyMaterial.EnableKeyword("_EMISSION");
            fireflyMaterial.SetColor("_EmissionColor", new Color(1f, 0.6f, 0f) * 2.0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = fireflyMaterial;

            spawnedFireflies.Add(ps);
        }

        private Vector3Int GetSectorCoordsFromTHCoords(Vector3 THCoords)
        {
            int x = Mathf.RoundToInt(THCoords.x / (float)SECTOR_SIZE);
            int y = Mathf.RoundToInt(THCoords.y / (float)SECTOR_SIZE);
            int z = Mathf.RoundToInt(THCoords.z / (float)SECTOR_SIZE);

            Vector3Int coords = new Vector3Int(x, y, z);
            return coords;
        }

        private Vector3 GetTHCoordsFromSector(Vector3Int SectorCoords)
        {
            float x = (float)(SectorCoords.x * SECTOR_SIZE);
            float y = (float)(SectorCoords.y * SECTOR_SIZE);
            float z = (float)(SectorCoords.z * SECTOR_SIZE);

            Vector3 coords = new Vector3(x, y, z);
            return coords;
        }

        private GameObject CreateTHSector(GameObject sectorHolder, Vector3Int sectorCoords)
        {
            // Used to group tree clones together for a cleaner hierachy
            GameObject sectorParent = new GameObject($"TH_Forest_Sector_{sectorCoords.x}_{sectorCoords.y}_{sectorCoords.z}");
            sectorParent.transform.SetParent(sectorHolder.transform, false);
            sectorParent.transform.localPosition = Vector3.zero;
            sectorParent.transform.localRotation = Quaternion.identity;

            // Used to group tree clones together for a cleaner hierachy
            GameObject treeParent = new GameObject("TH_Trees_Surface");
            treeParent.transform.SetParent(sectorParent.transform, false);
            treeParent.transform.localPosition = Vector3.zero;
            treeParent.transform.localRotation = Quaternion.identity;

            // Used to group grass clones together for a cleaner hierachy
            GameObject grassParent = new GameObject("TH_Grass_Surface");
            grassParent.transform.SetParent(sectorParent.transform, false);
            grassParent.transform.localPosition = Vector3.zero;
            grassParent.transform.localRotation = Quaternion.identity;

            // Used to group firefly clones together for a cleaner hierachy
            GameObject firefliesParent = new GameObject("TH_Fireflies_Surface");
            firefliesParent.transform.SetParent(sectorParent.transform, false);
            firefliesParent.transform.localPosition = Vector3.zero;
            firefliesParent.transform.localRotation = Quaternion.identity;

            return sectorParent;
        }

        private void ToggleTreeHitboxes(bool enabled)
        {
            foreach (GameObject tree in spawnedTrees)
            {
                tree.GetComponent<CapsuleCollider>()?.enabled = enabled;
            } 
        }

        private void UpdatePropDensity(string densityDescriptor, string spawnType)
        {
            if (spawnedTrees == null && spawnType == "tree")
            {
                ModHelper.Console.WriteLine("spawnedTrees is not initialized yet.", MessageType.Warning);
                return;
            }

            if (spawnedGrass == null && spawnType == "grass")
            {
                ModHelper.Console.WriteLine("spawnedGrass is not initialized yet.", MessageType.Warning);
                return;
            }

            if (spawnedFireflies == null && spawnType == "firefly")
            {
                ModHelper.Console.WriteLine("spawnedFireflies is not initialized yet.", MessageType.Warning);
                return;
            }

            int density = 0;

            switch (densityDescriptor)
            {
                case "Hidden":
                    if (spawnType == "tree") density = spawnedTrees.Count * 2;
                    if (spawnType == "grass") density = spawnedGrass.Count * 2;
                    if (spawnType == "firefly") density = spawnedFireflies.Count * 2;
                    break;
                case "Low":
                    density = 4;
                    break;
                case "Medium":
                    density = 3;
                    break;
                case "High":
                    density = 2;
                    break;
                case "Ultra":
                    density = 1;
                    break;
                default:
                    ModHelper.Console.WriteLine($"Unknown {spawnType} density setting: {density}", MessageType.Error);
                    return;
            }

            int propCount = 0;
            if (spawnType == "tree") propCount = spawnedTrees.Count;
            if (spawnType == "grass") propCount = spawnedGrass.Count;
            if (spawnType == "firefly") propCount = spawnedFireflies.Count;

            int spawnTicker = 0;

            for (int i = 0; i < propCount; i++)
            {
                if (spawnTicker >= density)
                {
                    if (spawnType == "tree") spawnedTrees[i].SetActive(true);
                    if (spawnType == "grass") spawnedGrass[i].SetActive(true);
                    if (spawnType == "firefly") spawnedFireflies[i].transform.gameObject.SetActive(true);

                    spawnTicker = 0;
                }
                else
                {
                    if (spawnType == "tree") spawnedTrees[i].SetActive(false);
                    if (spawnType == "grass") spawnedGrass[i].SetActive(false);
                    if (spawnType == "firefly") spawnedFireflies[i].transform.gameObject.SetActive(false);
                }

                spawnTicker++;
            }
        }

        private void StripQuantumComponents(GameObject obj)
        {
            foreach (var q in obj.GetComponentsInChildren<QuantumObject>(true)) Destroy(q);
            foreach (var q in obj.GetComponentsInChildren<SocketedQuantumObject>(true)) Destroy(q);
            foreach (var v in obj.GetComponentsInChildren<VisibilityObject>(true)) Destroy(v);
            foreach (var s in obj.GetComponentsInChildren<ShapeVisibilityTracker>(true)) Destroy(s);
        }

        public void Update()
        {
            // Hide trees and grass which are far away and disable far away colliders to help improve performance
            // Starting Benchmark (No Mod):  ~100fps on planet, ~80fps off planet
            // No Optimisation Benchmark: ~60fps on planet, ~50fps off planet
            // Optimisation Benchmark: ~80fps on planet, ~60fps off planet
            UpdateSectors();

            // Control whether each firefly group is currently visible
            UpdateFireflies();
        }

        private void UpdateSectors()
        {
            // Get Timber Hearth
            AstroObject THAstroObject = Locator.GetAstroObject(AstroObject.Name.TimberHearth);
            // Get the current active camera
            OWCamera playerCamera = Locator.GetActiveCamera();

            if (!THAstroObject || !playerCamera) return;

            Vector3 playerCoordsTHSpace = THAstroObject.transform.InverseTransformPoint(playerCamera.transform.position);
            Vector3Int playerSectorCoords = GetSectorCoordsFromTHCoords(playerCoordsTHSpace);

            float playerTHDistance = playerCoordsTHSpace.magnitude;

            const float MAX_DISTANCE = 800.0f;
            const float MIN_DISTANCE = 250.0f;

            // When the player is closer to Timber Hearth, more trees are hidden by the horizon
            const float FAR_DOT = -0.4f;
            const float CLOSE_DOT = 0.3f;

            float playerDistanceFract = Mathf.Clamp01((playerTHDistance - MIN_DISTANCE) / (MAX_DISTANCE - MIN_DISTANCE));
            float currentDot = Mathf.Lerp(CLOSE_DOT, FAR_DOT, playerDistanceFract);

            foreach (KeyValuePair<Vector3Int, GameObject> pair in propSectors)
            {
                GameObject sectorHolder = pair.Value;
                Vector3Int sectorCoords = pair.Key;

                // Props which are blocked from the player's view by Timber Hearth are hidden
                float dot = (float)Dot(sectorCoords, playerSectorCoords);
                bool isOccluded = dot < currentDot;

                sectorHolder.SetActive(!isOccluded);
            }
        }

        private void UpdateFireflies()
        {
            // Get the sun and Timber Hearth's world position
            AstroObject THAstroObject = Locator.GetAstroObject(AstroObject.Name.TimberHearth);
            AstroObject sunAstroObject = Locator.GetAstroObject(AstroObject.Name.Sun);

            OWRigidbody playerBody = Locator.GetPlayerBody();

            // If Timber Hearth, the sun or the player cannot be found then skip
            if (!THAstroObject || !sunAstroObject || !playerBody) return;

            Vector3 THPosition = THAstroObject.transform.position;
            Vector3 sunPos = sunAstroObject.transform.position;

            // Calculate the vector from Timber Hearth to the sun (no need to normalise)
            Vector3 sunDirFromTH = sunPos - THPosition;

            foreach (ParticleSystem fireflyPS in spawnedFireflies)
            {
                // Check whether the firefly effect is close enough to be enabled
                float distSqr = (fireflyPS.transform.position - playerBody.transform.position).sqrMagnitude;
                bool withinRange = distSqr < MAX_FIREFLY_DISTANCE * MAX_FIREFLY_DISTANCE;

                // Check if it is currently night, if not then disable
                Vector3 THWorldNormal = fireflyPS.transform.position - THPosition;

                // Calculate the dot product
                float dot = Dot(sunDirFromTH, THWorldNormal);
                bool isNight = dot < 0.0f;

                bool enabledAtNight = isNight && firefliesEnabledAtNight;
                bool enabledAtDay = !isNight && firefliesEnabledAtDay;

                bool isEnabled = withinRange && (enabledAtNight || enabledAtDay);

                // Get the main section of the particle system
                var main = fireflyPS.main;

                if (isEnabled && !main.loop)
                {
                    // Play the particle system
                    fireflyPS.Play();

                    // Enable looping
                    main.loop = true;
                }

                if (!isEnabled)
                {
                    // Disable looping
                    main.loop = false;

                    // Pause the particle system if there are no longer any particles left
                    if (fireflyPS.particleCount <= 0) fireflyPS.Pause();
                }
            }
        }

        private float Dot(Vector3 a, Vector3 b)
        {
            // Compute the dot product between vector a and b
            return (a.x * b.x + a.y * b.y + a.z * b.z);
        }

        private int Dot(Vector3Int a, Vector3Int b)
        {
            // Compute the dot product between vector a and b
            return (a.x * b.x + a.y * b.y + a.z * b.z);
        }

        private GameObject GetGameObjectAtPath(string path)
        {
            string[] stepNames = path.Split('/');

            // Get the first step in the path's corresponding GameObject
            GameObject go = FindRootObject(stepNames[0]);

            // If the first step doesn't exist then return null
            if (go == null)
            {
                ModHelper.Console.WriteLine($"Couldn't find object at path: {path}, failed to locate {stepNames[0]}", MessageType.Error);
                return null;
            }

            // Iterate through the remaining steps in the path and find the corresponding child GameObject at each step
            for (int i = 1; i < stepNames.Length; i++)
            {
                Transform next_step = null;

                // Check all the children for the net step
                foreach (Transform child in go.transform)
                {
                    if (child.name == stepNames[i])
                    {
                        next_step = child;
                        break;
                    }
                }

                // If the next step doesn't exist then return null
                if (next_step == null)
                {
                    ModHelper.Console.WriteLine($"Couldn't find object at path: {path}, failed to locate {stepNames[i]}", MessageType.Error);
                    return null;
                }

                // Update the current GameObject to the next step in the path
                go = next_step.gameObject;
            }

            // Return the final GameObject
            return go;
        }

        private GameObject FindRootObject(string name)
        {
            // Loop through each unity scene
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                // Get the current scene
                Scene scene = SceneManager.GetSceneAt(i);

                // If the scene is not loaded then skip
                if (!scene.isLoaded) continue;

                // Loop over each root component of the scene and try to find the wanted root
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == name) return root;
                }
            }

            return null;
        }

        private List<PropDetails> ParseJson(string json)
        {
            // Rest in peace 39097 line JSON file, you will be remembered

            // Prepare the list that will hold the prop details extracted from the JSON
            List<PropDetails> propDetailList = new List<PropDetails>();

            // Split JSON into seperate lines for easier processing
            string[] lines = json.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            PropDetails currentProp = null;

            foreach (string line in lines)
            {
                // Remove any leading or trailing whitespace
                string trimmedLine = line.Trim();

                // If the line doesn't contain both [ and ], it's not a line with position and rotation data, so skip
                if (!trimmedLine.Contains("[") || !trimmedLine.Contains("]")) continue;

                // Extract the position and rotation data
                string[] treeData = trimmedLine.Split(new char[] { '[', ']', ',' }, StringSplitOptions.RemoveEmptyEntries);

                // This shouldn't be called, but protects against bad data formatting
                // as treeData should consist of 3 position values and 3 rotation values
                if (treeData.Length != 6) continue;

                currentProp = new PropDetails();

                // Extract the prop position data
                float posX = float.Parse(treeData[0].Trim(), CultureInfo.InvariantCulture);
                float posY = float.Parse(treeData[1].Trim(), CultureInfo.InvariantCulture);
                float posZ = float.Parse(treeData[2].Trim(), CultureInfo.InvariantCulture);

                currentProp.position = new Vector3(posX, posY, posZ);

                // Extract the prop rotation data
                float rotX = float.Parse(treeData[3].Trim(), CultureInfo.InvariantCulture);
                float rotY = float.Parse(treeData[4].Trim(), CultureInfo.InvariantCulture);
                float rotZ = float.Parse(treeData[5].Trim(), CultureInfo.InvariantCulture);

                currentProp.rotation = new Vector3(rotX, rotY, rotZ);

                // Add the new prop to the list
                propDetailList.Add(currentProp);

                // Clear the current prop
                currentProp = null;
            }

            ModHelper.Console.WriteLine($"Parsed {propDetailList.Count} tree details.", MessageType.Success);

            return propDetailList;
        }

    }

}
