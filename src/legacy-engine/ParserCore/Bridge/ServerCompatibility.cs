// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;

namespace WaywardGamers.KParser.Bridge
{
    internal static class ServerCompatibility
    {
        private static readonly object StateLock = new object();
        private static string currentProfile = "sanctum";
        private static string petMappingPath = string.Empty;
        private static string localPlayerName = string.Empty;

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

        internal static bool IsHorizon
        {
            get { return string.Equals(CurrentProfile, "horizon", StringComparison.Ordinal); }
        }

        internal static bool SupportsCalculatedDots
        {
            get { return IsSanctumXi || IsHorizon; }
        }

        internal static string LocalPlayerName
        {
            get
            {
                lock (StateLock)
                    return localPlayerName;
            }
        }

        internal static void Configure(string requestedProfile, string requestedPetMappingPath)
        {
            string profile = NormalizeProfile(requestedProfile);
            string mappingPath = requestedPetMappingPath == null
                ? string.Empty
                : requestedPetMappingPath.Trim();
            bool profileChanged;

            lock (StateLock)
            {
                profileChanged = string.Equals(
                    currentProfile,
                    profile,
                    StringComparison.Ordinal) == false;
                currentProfile = profile;
                petMappingPath = mappingPath;
            }

            // Captured stats belong to a character on one server profile. Do not
            // let a same-named character inherit them after a profile switch.
            if (profileChanged)
                SanctumDotProfileStore.Clear();
        }

        internal static void ConfigureLocalPlayer(string requestedPlayerName)
        {
            string playerName = requestedPlayerName == null
                ? string.Empty
                : requestedPlayerName.Trim();
            if (playerName.Length > 32)
                playerName = string.Empty;

            lock (StateLock)
                localPlayerName = playerName;
        }

        internal static string NormalizeProfile(string requestedProfile)
        {
            if (requestedProfile == null || requestedProfile.Trim().Length == 0)
                return "sanctum";

            string profile = requestedProfile.Trim().ToLowerInvariant();
            if (profile == "sanctum" ||
                profile == "sanctumxi" ||
                profile == "sanctum-xi" ||
                profile == "sanctum xi")
            {
                return "sanctum";
            }

            if (profile == "horizon" ||
                profile == "horizonxi" ||
                profile == "horizon-xi" ||
                profile == "horizon xi")
            {
                return "horizon";
            }

            return "other";
        }
    }
}
