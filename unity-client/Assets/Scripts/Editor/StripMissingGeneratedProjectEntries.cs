using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;

namespace CastleDefender.Editor
{
    internal sealed class StripMissingGeneratedProjectEntries : AssetPostprocessor
    {
        static string OnGeneratedCSProject(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(content))
                return content;

            try
            {
                var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
                XElement root = document.Root;
                if (root == null)
                    return content;

                XNamespace ns = root.Name.Namespace;
                string projectDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
                bool changed = false;

                var compileNodes = root
                    .Descendants(ns + "Compile")
                    .Where(element => element.Attribute("Include") != null)
                    .ToList();

                foreach (XElement compileNode in compileNodes)
                {
                    string includePath = compileNode.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(includePath))
                        continue;

                    string absolutePath = Path.GetFullPath(Path.Combine(projectDirectory, includePath));
                    if (File.Exists(absolutePath))
                        continue;

                    compileNode.Remove();
                    changed = true;
                }

                return changed
                    ? document.ToString(SaveOptions.DisableFormatting)
                    : content;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[StripMissingGeneratedProjectEntries] Failed to sanitize generated project '{path}': {ex.Message}");
                return content;
            }
        }
    }
}
