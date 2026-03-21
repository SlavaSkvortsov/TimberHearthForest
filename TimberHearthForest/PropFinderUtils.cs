using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimberHearthForest
{
    internal class PropFinderUtils
    {
        public static GameObject GetGameObjectAtPath(string path, IModConsole console)
        {
            string[] stepNames = path.Split('/');

            // Get the first step in the path's corresponding GameObject
            GameObject go = FindRootObject(stepNames[0]);

            // If the first step doesn't exist then return null
            if (go == null)
            {
                console.WriteLine($"Couldn't find object at path: {path}, failed to locate {stepNames[0]}", MessageType.Error);
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
                    console.WriteLine($"Couldn't find object at path: {path}, failed to locate {stepNames[i]}", MessageType.Error);
                    return null;
                }

                // Update the current GameObject to the next step in the path
                go = next_step.gameObject;
            }

            // Return the final GameObject
            return go;
        }

        private static GameObject FindRootObject(string name)
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
    }
}
