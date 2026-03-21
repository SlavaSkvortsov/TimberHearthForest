using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static TimberHearthForest.TimberHearthForest;

namespace TimberHearthForest
{
    internal class FileLoadingUtils
    {
        public static Texture2D LoadTexture(string filePath, IModConsole console)
        {
            if (!File.Exists(filePath))
            {
                console.WriteLine($"Failed to load file at path: {filePath}", MessageType.Error);
                return null;
            }

            byte[] fileData = File.ReadAllBytes(filePath);

            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(fileData)) return texture;

            return null;
        }

        public static List<PropDetails> LoadAndParseJSON(string fileLoc)
        {
            // Rest in peace 39097 line JSON file, you will be remembered
            string json = File.ReadAllText(fileLoc);

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

            return propDetailList;
        }
    }
}
