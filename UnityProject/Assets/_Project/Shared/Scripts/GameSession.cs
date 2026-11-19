using UnityEngine;

namespace UnityLab.Shared
{
    /// <summary>
    /// Settings chosen in the main menu that the next scene needs (who you are, which server).
    /// Plain static data instead of a ScriptableObject asset, so playing in the Editor never
    /// modifies project files. The connection settings are remembered between sessions.
    /// </summary>
    public static class GameSession
    {
        const string NicknameKey = "UnityLab.Nickname";
        const string HostKey = "UnityLab.ServerHost";
        const string PortKey = "UnityLab.ServerPort";

        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 7777;

        public static string Nickname
        {
            get => PlayerPrefs.GetString(NicknameKey, "Player");
            set => PlayerPrefs.SetString(NicknameKey, value);
        }

        public static string ServerHost
        {
            get => PlayerPrefs.GetString(HostKey, DefaultHost);
            set => PlayerPrefs.SetString(HostKey, value);
        }

        public static int ServerPort
        {
            get => PlayerPrefs.GetInt(PortKey, DefaultPort);
            set => PlayerPrefs.SetInt(PortKey, value);
        }

        /// <summary>When true the player's car drives itself. Used by the automated smoke test.</summary>
        public static bool Autopilot { get; set; }

        /// <summary>Online play needs raw TCP sockets, which browsers do not offer.</summary>
        public static bool SupportsOnlinePlay => Application.platform != RuntimePlatform.WebGLPlayer;

        public static void Save() => PlayerPrefs.Save();
    }
}
