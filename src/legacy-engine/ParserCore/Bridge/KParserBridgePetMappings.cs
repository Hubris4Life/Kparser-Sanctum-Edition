// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.IO;

namespace WaywardGamers.KParser.Bridge
{
    internal static class KParserBridgePetMappings
    {
        private static readonly object CacheLock = new object();
        private static string cachedPath = string.Empty;
        private static DateTime cachedWriteUtc = DateTime.MinValue;
        private static Dictionary<string, string> owners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> ambiguousPets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static long Revision
        {
            get
            {
                EnsureLoaded(ServerCompatibility.PetMappingPath);
                lock (CacheLock)
                    return cachedWriteUtc.Ticks;
            }
        }

        internal static bool IsMappedPet(string petName)
        {
            string owner;
            return TryResolveOwner(petName, out owner);
        }

        internal static bool TryResolveOwner(string petName, out string ownerName)
        {
            ownerName = string.Empty;
            if (petName == null || petName.Trim().Length == 0)
                return false;

            EnsureLoaded(ServerCompatibility.PetMappingPath);
            lock (CacheLock)
            {
                return ambiguousPets.Contains(petName) == false &&
                       owners.TryGetValue(petName, out ownerName);
            }
        }

        private static void EnsureLoaded(string path)
        {
            string normalizedPath = string.Empty;
            if (path != null && path.Trim().Length > 0)
            {
                try
                {
                    normalizedPath = Path.GetFullPath(path);
                }
                catch (Exception)
                {
                    normalizedPath = string.Empty;
                }
            }
            DateTime writeUtc = File.Exists(normalizedPath)
                ? File.GetLastWriteTimeUtc(normalizedPath)
                : DateTime.MinValue;

            lock (CacheLock)
            {
                if (string.Equals(cachedPath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    cachedWriteUtc == writeUtc)
                {
                    return;
                }

                Dictionary<string, string> loadedOwners =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> loadedAmbiguous =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (writeUtc != DateTime.MinValue)
                {
                    try
                    {
                        foreach (string line in File.ReadAllLines(normalizedPath))
                        {
                            if (line == null || line.Trim().Length == 0 || line.StartsWith("#"))
                                continue;

                            string[] fields = line.Split('\t');
                            if (fields.Length < 5)
                                continue;

                            string petName = fields[2].Trim();
                            string ownerName = fields[4].Trim();
                            if (petName.Length == 0 || ownerName.Length == 0)
                                continue;

                            string existingOwner;
                            if (loadedOwners.TryGetValue(petName, out existingOwner) &&
                                string.Equals(existingOwner, ownerName, StringComparison.OrdinalIgnoreCase) == false)
                            {
                                loadedOwners.Remove(petName);
                                loadedAmbiguous.Add(petName);
                            }
                            else if (loadedAmbiguous.Contains(petName) == false)
                            {
                                loadedOwners[petName] = ownerName;
                            }
                        }
                    }
                    catch (IOException)
                    {
                        loadedOwners.Clear();
                        loadedAmbiguous.Clear();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        loadedOwners.Clear();
                        loadedAmbiguous.Clear();
                    }
                }

                cachedPath = normalizedPath;
                cachedWriteUtc = writeUtc;
                owners = loadedOwners;
                ambiguousPets = loadedAmbiguous;
            }
        }
    }
}
