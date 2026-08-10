// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;

namespace WaywardGamers.KParser.Bridge
{
    internal static class ServerCompatibility
    {
        private static readonly object StateLock = new object();
        private static string currentProfile = "sanctum";
        private static string petMappingPath = string.Empty;

        internal static string CurrentProfile
        {
            get
            {
                lock (StateLock)
                    return currentProfile;
            }
        }

        internal static string PetMappingPath
        {
            get
            {
                lock (StateLock)
                    return petMappingPath;
            }
        }

        internal static bool IsSanctumXi
        {
            get { return string.Equals(CurrentProfile, "sanctum", StringComparison.Ordinal); }
        }

        internal static bool SupportsCalculatedDots
        {
            get { return IsSanctumXi; }
        }

        internal static void Configure(string requestedProfile, string requestedPetMappingPath)
        {
            string profile = NormalizeProfile(requestedProfile);
            string mappingPath = requestedPetMappingPath == null
                ? string.Empty
                : requestedPetMappingPath.Trim();

            lock (StateLock)
            {
                currentProfile = profile;
                petMappingPath = mappingPath;
            }
        }

        internal static string NormalizeProfile(string requestedProfile)
        {
            if (requestedProfile == null || requestedProfile.Trim().Length == 0)
                return "sanctum";

            string profile = requestedProfile.Trim().ToLowerInvariant();
            return profile == "sanctum" ||
                   profile == "sanctumxi" ||
                   profile == "sanctum-xi"
                ? "sanctum"
                : "other";
        }
    }
}
