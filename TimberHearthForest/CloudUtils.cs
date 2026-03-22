using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace TimberHearthForest
{
    internal class CloudUtils
    {
        private static string modFolderPath = "";

        private static IModConsole modConsole;

        public static void SetModDirectoryPath(string dirPath)
        {
            modFolderPath = dirPath;
        }

        public static void SetModConsole(IModConsole console)
        {
            modConsole = console;
        }

        public static void CreateCloud(GameObject cloudHolder, string textureName, string normalName, float cloudSpeed, bool isOutwardFacing, ref List<GameObject> cloudObjects, ref List<float> cloudVelocities)
        {
            // Load the cloud texture
            Texture2D albedoMap = FileLoadingUtils.LoadTexture(modFolderPath + "Assets/" + textureName + ".png", modConsole);

            if (albedoMap == null)
            {
                modConsole.WriteLine($"Failed to load the cloud texture file: {textureName}", MessageType.Error);
                return;
            }

            Shader standardShader = Shader.Find("Standard");

            if (standardShader == null)
            {
                modConsole.WriteLine($"Failed to locate the Standard material shader for {textureName}", MessageType.Error);
                return;
            }

            // Create the cloud material
            Material mat = new Material(standardShader);

            // Set rendering mode to transparent
            mat.SetFloat("_Mode", 3);

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            mat.mainTexture = albedoMap;
            mat.color = new Color(1.0f, 1.0f, 1.0f, 1.0f);

            // Load the cloud texture
            Texture2D normalMap = FileLoadingUtils.LoadTexture(modFolderPath + "Assets/" + normalName + ".png", modConsole);

            if (normalMap == null)
            {
                modConsole.WriteLine("Failed to load the cloud normal texture file", MessageType.Error);
            }
            else
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", normalMap);
                mat.SetFloat("_BumpScale", 0.8f);
            }

            // Create the cloud sphere
            GameObject cloudSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cloudSphere.transform.SetParent(cloudHolder.transform, false);

            cloudSphere.GetComponent<MeshFilter>()?.mesh = CreateSphereMesh(32, 32, 1.0f);

            // Invert the normals of the mesh if the cloud faces outwards towards space
            if (isOutwardFacing) cloudSphere.GetComponent<MeshFilter>().mesh = InvertMesh(cloudSphere.GetComponent<MeshFilter>().mesh);

            cloudSphere.name = isOutwardFacing ? "TH_Clouds_Out" : "TH_Clouds_In";
            cloudSphere.GetComponent<SphereCollider>()?.enabled = false;

            cloudSphere.transform.localPosition = Vector3.zero;
            cloudSphere.transform.localRotation = Quaternion.identity;
            cloudSphere.transform.localScale = Vector3.one * 295.0f;

            MeshRenderer cloudRenderer = cloudSphere.GetComponent<MeshRenderer>();
            cloudRenderer.material = mat;
            cloudRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cloudRenderer.receiveShadows = true;

            // If the cloud faces towards space then the normal map should be inverted
            if (isOutwardFacing) cloudRenderer.material.SetFloat("_BumpScale", -0.8f);

            // Store the in facing cloud sphere renderer
            cloudObjects.Add(cloudSphere);
            cloudVelocities.Add(cloudSpeed);
        }

        private static Mesh InvertMesh(Mesh original)
        {
            Mesh mesh = UnityEngine.GameObject.Instantiate(original);

            // Flip normals
            for (int i = 0; i < mesh.normals.Length; i++) mesh.normals[i] = -mesh.normals[i];

            // Flip triangle winding
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int[] triangles = mesh.GetTriangles(i);

                for (int j = 0; j < triangles.Length; j += 3)
                {
                    // swap 0 and 1
                    int temp = triangles[j];
                    triangles[j] = triangles[j + 1];
                    triangles[j + 1] = temp;
                }

                mesh.SetTriangles(triangles, i);
            }

            return mesh;
        }

        private static Mesh CreateSphereMesh(int latitudeSegments, int longitudeSegments, float radius)
        {
            Mesh mesh = new Mesh();

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float a1 = Mathf.PI * lat / latitudeSegments;
                float sin1 = Mathf.Sin(a1);
                float cos1 = Mathf.Cos(a1);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float a2 = 2 * Mathf.PI * lon / longitudeSegments;
                    float sin2 = Mathf.Sin(a2);
                    float cos2 = Mathf.Cos(a2);

                    Vector3 pos = new Vector3(sin1 * cos2, cos1, sin1 * sin2) * radius;

                    vertices.Add(pos);
                    normals.Add(pos.normalized);
                    uvs.Add(new Vector2((float)lon / longitudeSegments, (float)lat / latitudeSegments));
                }
            }

            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int current = lat * (longitudeSegments + 1) + lon;
                    int next = current + longitudeSegments + 1;

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);

                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);

            return mesh;
        }
    }
}
