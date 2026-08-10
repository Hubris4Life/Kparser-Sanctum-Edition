// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;

namespace WaywardGamers.KParser.Parsing
{
    /// <summary>
    /// Decodes the client-visible pet ownership names emitted by the Sanctum server.
    /// The unmodified pet name and entity ID remain authoritative inside the server.
    /// </summary>
    internal static class SanctumPetName
    {
        internal const char OwnerSeparator = '@';
        internal const int OwnerTokenLength = 5;
        private const string PossessiveSeparator = "'s ";

        internal static bool TryParse(
            string decoratedName,
            out string petName,
            out string ownerReference)
        {
            petName = string.Empty;
            ownerReference = string.Empty;

            if (string.IsNullOrEmpty(decoratedName))
                return false;

            int possessiveIndex = decoratedName.IndexOf(
                PossessiveSeparator,
                StringComparison.Ordinal);
            if (possessiveIndex > 0)
            {
                string expandedOwner = decoratedName.Substring(0, possessiveIndex);
                string expandedPet = decoratedName.Substring(
                    possessiveIndex + PossessiveSeparator.Length);
                if (IsValidOwnerReference(expandedOwner) &&
                    IsValidPetPart(expandedPet))
                {
                    petName = expandedPet;
                    ownerReference = expandedOwner;
                    return true;
                }
            }

            int separatorIndex = decoratedName.LastIndexOf(OwnerSeparator);
            if (separatorIndex <= 0 || separatorIndex == decoratedName.Length - 1)
                return false;

            string candidatePet = decoratedName.Substring(0, separatorIndex);
            string candidateOwner = decoratedName.Substring(separatorIndex + 1);
            if (IsValidPetPart(candidatePet) == false ||
                IsValidOwnerReference(candidateOwner) == false)
            {
                return false;
            }

            petName = candidatePet;
            ownerReference = candidateOwner;
            return true;
        }

        internal static string GetOwnerToken(string ownerName)
        {
            uint hash = 2166136261U;
            string normalized = ownerName ?? string.Empty;
            for (int index = 0; index < normalized.Length; index++)
            {
                char character = normalized[index];
                if (character >= 'A' && character <= 'Z')
                    character = (char)(character + ('a' - 'A'));

                hash ^= (byte)character;
                hash *= 16777619U;
            }

            return (hash & 0xFFFFFU).ToString("X5");
        }

        internal static bool LooksLikeOwnerToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != OwnerTokenLength)
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character < '0' || character > '9') &&
                    (character < 'A' || character > 'F') &&
                    (character < 'a' || character > 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidPetPart(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsLetterOrDigit(character) == false &&
                    character != '_' && character != '-' && character != '\'' &&
                    character != ' ' && character != '.')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidOwnerReference(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 15)
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsLetterOrDigit(value[index]) == false)
                    return false;
            }

            return true;
        }
    }
}
