using System.Collections.Generic;
using System.Text;

namespace UnityLab.Racing.Protocol
{
    /// <summary>Rules for the names players type in the menu. Shared by client and server.</summary>
    public static class Nickname
    {
        public const string Default = "Player";

        /// <summary>Removes control characters, collapses whitespace and limits the length.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Default;

            var builder = new StringBuilder(ProtocolConstants.MaxNicknameLength);
            bool lastWasSpace = false;
            foreach (char c in raw.Trim())
            {
                // Tabs and newlines become spaces; other control characters are dropped.
                bool isSpace = char.IsWhiteSpace(c);
                if (!isSpace && (char.IsControl(c) || char.IsSurrogate(c)))
                    continue;

                if (isSpace && lastWasSpace)
                    continue;

                builder.Append(isSpace ? ' ' : c);
                lastWasSpace = isSpace;
                if (builder.Length == ProtocolConstants.MaxNicknameLength)
                    break;
            }

            string clean = builder.ToString().Trim();
            return clean.Length == 0 ? Default : clean;
        }

        /// <summary>Appends " 2", " 3"... when the name is already taken (case-insensitive).</summary>
        public static string MakeUnique(string nickname, ICollection<string> taken)
        {
            var lowerTaken = new HashSet<string>();
            foreach (string name in taken)
                lowerTaken.Add(name.ToLowerInvariant());

            if (!lowerTaken.Contains(nickname.ToLowerInvariant()))
                return nickname;

            for (int suffix = 2; ; suffix++)
            {
                string tail = " " + suffix;
                string head = nickname.Length + tail.Length > ProtocolConstants.MaxNicknameLength
                    ? nickname.Substring(0, ProtocolConstants.MaxNicknameLength - tail.Length).TrimEnd()
                    : nickname;
                string candidate = head + tail;
                if (!lowerTaken.Contains(candidate.ToLowerInvariant()))
                    return candidate;
            }
        }
    }
}
