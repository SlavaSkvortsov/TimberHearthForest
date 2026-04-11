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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TimberHearthForest
{
    public class TimberHearthForest : ModBehaviour
    {
        public static TimberHearthForest Instance;

        private const float MAX_FIREFLY_DISTANCE = 100.0f;

        private Dictionary<Vector3Int, ForestSectorUtils.ForestSector> forestSectors = new Dictionary<Vector3Int, ForestSectorUtils.ForestSector>();

        private List<GameObject> spawnedTrees = new List<GameObject>();
        private List<GameObject> spawnedGrass = new List<GameObject>();

        private List<ParticleSystem> spawnedFireflies = new List<ParticleSystem>();

        private List<GameObject> cloudObjects = new List<GameObject>();
        private List<float> cloudVelocities = new List<float>();

        private GameObject THSatelliteObject;

        private Sector timberHearthSector;
        private Sector quantumMoonSector;

        private bool forestSectorOptimisationEnabled = true;

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

        /// Called by OWML, once at the start and upon each config setting change.
        public override void Configure(IModConfig config)
        {
            // Update the tree density
            string treeDensityPreset = config.GetSettingsValue<string>("treeDensity");
            UpdatePropDensity(treeDensityPreset, "tree");

            // Update whether tree colliders are enabled
            bool treeCollidersEnabled = config.GetSettingsValue<string>("treeCollidersEnabled") == "Enabled";
            ToggleTreeHitboxes(treeCollidersEnabled);

            // Update sector based on whether the sector optimisation is enabled
            forestSectorOptimisationEnabled = ModHelper.Config.GetSettingsValue<string>("treeOcclusionOptimisation") == "Enabled";

            // Update the grass density
            string grassDensityPreset = config.GetSettingsValue<string>("grassDensity");
            UpdatePropDensity(grassDensityPreset, "grass");

            // Update when fireflies should be active
            string fireflyEnabledState = config.GetSettingsValue<string>("fireflyEnabled");
            firefliesEnabledAtNight = fireflyEnabledState == "Night" || fireflyEnabledState == "Day and Night";
            firefliesEnabledAtDay = fireflyEnabledState == "Day" || fireflyEnabledState == "Day and Night";

            // Update the firefly emitter density
            string fireflyDensityPreset = config.GetSettingsValue<string>("fireflyDensity");
            UpdatePropDensity(fireflyDensityPreset, "firefly");

            // Update whether clouds are enabled
            string cloudDensityPreset = config.GetSettingsValue<string>("cloudDensity");
            UpdateCloudDensity(cloudDensityPreset);
        }

        private void LoadAndSpawnProps(string spawnDataFileLoc)
        {
            // If the tree spawn data file doesn't exist then exit
            if (!System.IO.File.Exists(spawnDataFileLoc))
            {
                ModHelper.Console.WriteLine($"Couldn't find {spawnDataFileLoc}", MessageType.Error);
                return;
            }

            // Load the tree spawn data and convert it to a list of PropDetails
            List<PropDetails> spawnData = FileLoadingUtils.LoadAndParseJSON(spawnDataFileLoc);

            ModHelper.Console.WriteLine($"Parsed {spawnData.Count} tree details.", MessageType.Success);

            // If the spawn data is null then exit
            if (spawnData == null)
            {
                ModHelper.Console.WriteLine("Loaded data is null.", MessageType.Error);
                return;
            }

            // Spawn the trees on the surface of Timber Hearth
            StartCoroutine(SpawnTrees(spawnData));

            // Setup clouds around Timber Hearth
            SpawnAndSetupClouds();
        }

        private void SpawnAndSetupClouds()
        {
            // Clear the stored cloud renderers
            cloudObjects = new List<GameObject>();
            cloudVelocities = new List<float>();

            AstroObject timberHearthAstroObject = Locator.GetAstroObject(AstroObject.Name.TimberHearth);
            GameObject cloudHolder = timberHearthAstroObject?.GetComponentInChildren<Sector>()?.transform.gameObject;

            if (cloudHolder == null)
            {
                ModHelper.Console.WriteLine("Couldn't locate the Timber Hearth Sector", MessageType.Error);
                return;
            }

            CloudUtils.SetModDirectoryPath(ModHelper.Manifest.ModFolderPath);
            CloudUtils.SetModConsole(ModHelper.Console);

            CloudUtils.LoadCloudsAssetBundle();

            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds", "timberHearthCloudsNormal", 0.0017f, true, ref cloudObjects, ref cloudVelocities);
            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds", "timberHearthCloudsNormal", 0.0017f, false, ref cloudObjects, ref cloudVelocities);

            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds2", "timberHearthCloudsNormal2", 0.0025f, true, ref cloudObjects, ref cloudVelocities);
            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds2", "timberHearthCloudsNormal2", 0.0025f, false, ref cloudObjects, ref cloudVelocities);

            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds3", "timberHearthCloudsNormal3", 0.0034f, true, ref cloudObjects, ref cloudVelocities);
            CloudUtils.CreateCloud(cloudHolder, 295.0f, "timberHearthClouds3", "timberHearthCloudsNormal3", 0.0034f, false, ref cloudObjects, ref cloudVelocities);

            // Apply the initial cloud visibility setting
            string cloudDensityPreset = ModHelper.Config.GetSettingsValue<string>("cloudDensity");
            UpdateCloudDensity(cloudDensityPreset);
        }

        private void UpdateCloudDensity(string cloudDensityPreset)
        {
            int cloudDensity = 0;

            switch (cloudDensityPreset)
            {
                case "High":    cloudDensity = 3; break;
                case "Medium":  cloudDensity = 2; break;
                case "Low":     cloudDensity = 1; break;
                case "Hidden":  cloudDensity = 0; break;
                default:
                    ModHelper.Console.WriteLine($"Unknown cloud density setting: {cloudDensity}", MessageType.Error);
                    return;
            }

            for (int i = 0; i < cloudObjects.Count; i++)
            {
                cloudObjects[i]?.SetActive(i < cloudDensity * 2);
            }
        }

        IEnumerator SpawnTrees(List<PropDetails> treeData)
        {
            // Clear the forest sectors
            forestSectors = new Dictionary<Vector3Int, ForestSectorUtils.ForestSector>();

            // Clear the stored trees, grass tufts and particle systems
            spawnedTrees = new List<GameObject>();
            spawnedGrass = new List<GameObject>();

            spawnedFireflies = new List<ParticleSystem>();

            // Wait for scene to load
            yield return new WaitForSeconds(3f);

            // Locate TimberHearth_Body
            //GameObject timberHearthBody = GameObject.Find("TimberHearth_Body");
            GameObject timberHearthBody = Locator.GetAstroObject(AstroObject.Name.TimberHearth)?.transform.gameObject;

            if (timberHearthBody == null)
            {
                ModHelper.Console.WriteLine("Couldn't locate the TimberHearth_Body gameobject", MessageType.Error);
                yield break;
            }

            ModHelper.Console.WriteLine("Located TimberHearth_Body object successfully", MessageType.Success);

            // Get Timber Hearth's mapping satellite
            THSatelliteObject = GameObject.Find("Satellite_Body");

            // Locate the tree template gameobject
            const string treeTemplatePath = "QuantumMoon_Body/Sector_QuantumMoon/State_TH/Interactables_THState/Crater_Surface/Surface_AlpineTrees_Single/QAlpine_Tree_.25 (1)";
            GameObject treeTemplate = PropFinderUtils.GetGameObjectAtPath(treeTemplatePath, ModHelper.Console);

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
            GameObject grassTemplate = PropFinderUtils.GetGameObjectAtPath(grassTemplatePath, ModHelper.Console);

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
                Vector3Int sectorCoords = ForestSectorUtils.GetSectorCoordsFromTHCoords(THLocalCoords);

                Vector3 detailWorldCoords = timberHearthBody.transform.TransformPoint(THLocalCoords);

                // Check if the sector exists
                if (!forestSectors.ContainsKey(sectorCoords))
                {
                    // Create the new sector
                    forestSectors[sectorCoords] = ForestSectorUtils.CreateSector(sectorsParent, sectorCoords);
                }

                GameObject sectorParent = forestSectors[sectorCoords].sectorParent;
                Transform treeParent = sectorParent.transform.Find("TH_Trees_Surface");
                Transform grassParent = sectorParent.transform.Find("TH_Grass_Surface");
                Transform firefliesParent = sectorParent.transform.Find("TH_Fireflies_Surface");

                // Spawn the tree
                GameObject treeClone = Instantiate(treeTemplate);

                // Parent the tree
                treeClone.transform.SetParent(treeParent, false);

                // Remove quantum components to prevent weird interactions with the tree clones
                RemoveQuantumComponents(treeClone);

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

            ModHelper.Console.WriteLine("All trees, grass tufts and fireflies have been created successfully.", MessageType.Success);

            // Update the tree density
            string treeDensityPreset = ModHelper.Config.GetSettingsValue<string>("treeDensity");
            UpdatePropDensity(treeDensityPreset, "tree");

            // Update the state of tree colliders
            bool treeCollidersEnabled = ModHelper.Config.GetSettingsValue<string>("treeCollidersEnabled") == "Enabled";
            ToggleTreeHitboxes(treeCollidersEnabled);

            // Update sector based on whether the sector optimisation is enabled
            forestSectorOptimisationEnabled = ModHelper.Config.GetSettingsValue<string>("treeOcclusionOptimisation") == "Enabled";

            // Update the grass density
            string grassDensityPreset = ModHelper.Config.GetSettingsValue<string>("grassDensity");
            UpdatePropDensity(treeDensityPreset, "grass");

            // Update firefly visibility
            string fireflyEnabledState = ModHelper.Config.GetSettingsValue<string>("fireflyEnabled");
            firefliesEnabledAtNight = fireflyEnabledState == "Night" || fireflyEnabledState == "Day and Night";
            firefliesEnabledAtDay = fireflyEnabledState == "Day" || fireflyEnabledState == "Day and Night";

            // Update the firefly density
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

        private void ToggleTreeHitboxes(bool enabled)
        {
            // Loop over each tree and toggle its hitbox
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

            int propCount = 0;
            if (spawnType == "tree") propCount = spawnedTrees.Count;
            if (spawnType == "grass") propCount = spawnedGrass.Count;
            if (spawnType == "firefly") propCount = spawnedFireflies.Count;

            int density = 0;

            switch (densityDescriptor)
            {
                case "Ultra":   density = 1; break;
                case "High":    density = 2; break;
                case "Medium":  density = 3; break;
                case "Low":     density = 4; break;
                case "Hidden":  density = propCount * 2; break;
                default:
                    ModHelper.Console.WriteLine($"Unknown {spawnType} density setting: {density}", MessageType.Error);
                    return;
            }

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

        private void RemoveQuantumComponents(GameObject obj)
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

            // Scroll the cloud textures
            UpdateClouds();
        }

        private void UpdateSectors()
        {
            if (!forestSectorOptimisationEnabled)
            {
                foreach (KeyValuePair<Vector3Int, ForestSectorUtils.ForestSector> pair in forestSectors) pair.Value.sectorParent.SetActive(true);
                return;
            }

            // Get Timber Hearth
            AstroObject THAstroObject = Locator.GetAstroObject(AstroObject.Name.TimberHearth);
            // Get the current active camera
            OWCamera playerCamera = Locator.GetActiveCamera();
            // Get the player's surveyor probe
            SurveyorProbe playerProbe = Locator.GetProbe();

            if (!THAstroObject || !playerCamera) return;

            Vector3 playerCoordsTHSpace = THAstroObject.transform.InverseTransformPoint(playerCamera.transform.position);
            Vector3Int playerSectorCoords = ForestSectorUtils.GetSectorCoordsFromTHCoords(playerCoordsTHSpace);

            // Calculate the player's distance from Timber Hearth
            float playerTHDistance = playerCoordsTHSpace.magnitude;

            // Fill the array initially with the player's location
            Vector3Int[] cameraSectorLocations = { playerSectorCoords, playerSectorCoords, playerSectorCoords };

            // If the satellite exists, calculate its Timber Hearth sector grid coordinates
            if (THSatelliteObject)
            {
                Vector3 satelliteCoordsTHSpace = THAstroObject.transform.InverseTransformPoint(THSatelliteObject.transform.position);
                Vector3Int satelliteSectorCoords = ForestSectorUtils.GetSectorCoordsFromTHCoords(satelliteCoordsTHSpace);
                // Add the satellites sector location to the camera locations list
                cameraSectorLocations[1] = satelliteSectorCoords;
            }

            // If the probe exists, calculate its Timber Hearth sector grid coordinates
            if (playerProbe)
            {
                Vector3 probeCoordsTHSpace = THAstroObject.transform.InverseTransformPoint(playerProbe.transform.position);
                Vector3Int probeSectorCoords = ForestSectorUtils.GetSectorCoordsFromTHCoords(probeCoordsTHSpace);
                // Add the probes sector location to the camera locations list
                cameraSectorLocations[2] = probeSectorCoords;
            }

            foreach (KeyValuePair<Vector3Int, ForestSectorUtils.ForestSector> pair in forestSectors)
            {
                // Occlusion only occurs when neither the probe, satellite or player can see the tree group
                bool isVisible = ForestSectorUtils.IsSectorVisible(pair.Value, cameraSectorLocations, playerTHDistance);
                pair.Value.sectorParent.SetActive(isVisible);
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

        private void UpdateClouds()
        {
            if (cloudObjects == null) return;

            Transform activeCamTransform = Locator.GetActiveCamera()?.transform;
            Transform timberHearthTransform = Locator.GetAstroObject(AstroObject.Name.TimberHearth)?.transform;

            float playerTHDistance = 0.0f;

            if (activeCamTransform != null && timberHearthTransform != null)
            {
                playerTHDistance = Vector3.Distance(activeCamTransform.position, timberHearthTransform.position);
                playerTHDistance -= CloudUtils.MAX_CLOUD_SPHERE_RADIUS - 10.0f;
                playerTHDistance = Mathf.Clamp01(playerTHDistance * 0.01f);
            }

            for (int i = 0; i < cloudObjects.Count; i++)
            {
                try
                {
                    Material cloudMaterial = cloudObjects[i]?.transform.GetComponent<MeshRenderer>()?.material;
                    cloudMaterial?.mainTextureOffset = new Vector2(Time.time * cloudVelocities[i], 0);

                    // Only show in facing clouds when the player is in the atmosphere
                    if (cloudObjects[i].name.Contains("_In"))
                    {
                        cloudMaterial?.color = new Color(1.0f, 1.0f, 1.0f, 1.0f - playerTHDistance);
                    }

                    // When the player is below the cloud layer, then each of the 3 cloud layers are spaced apart
                    float baseScale = CloudUtils.MAX_CLOUD_SPHERE_RADIUS;
                    float groundScale = baseScale + Mathf.Floor(i / 2.0f) * 15.0f;

                    float scaleWithDistance = Mathf.Lerp(groundScale, baseScale, playerTHDistance);

                    cloudObjects[i]?.transform.localScale = Vector3.one * scaleWithDistance;
                }
                catch
                {
                    // This occurs when the player quits the game to the main menu as the cloud gameobjects are all null
                    continue;
                }
            }
        }

        private float Dot(Vector3 a, Vector3 b)
        {
            // Compute the dot product between vector a and b
            return (a.x * b.x + a.y * b.y + a.z * b.z);
        }

    }

}
