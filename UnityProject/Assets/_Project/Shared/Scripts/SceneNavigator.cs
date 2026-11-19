using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityLab.Shared
{
    /// <summary>Names of the scenes in the build, so they are not typed as strings everywhere.</summary>
    public static class Scenes
    {
        public const string MainMenu = "MainMenu";
        public const string RacingSinglePlayer = "SinglePlayer";
        public const string RacingOnline = "Online";
        public const string RtsBattlefield = "Battlefield";
    }

    /// <summary>Scene changes. Also usable from UI buttons through the component instance methods.</summary>
    public class SceneNavigator : MonoBehaviour
    {
        public static void Load(string sceneName) => SceneManager.LoadScene(sceneName);

        public static void BackToMenu() => Load(Scenes.MainMenu);

        public void LoadRacingSinglePlayer() => Load(Scenes.RacingSinglePlayer);
        public void LoadRacingOnline() => Load(Scenes.RacingOnline);
        public void LoadRts() => Load(Scenes.RtsBattlefield);
        public void LoadMainMenu() => BackToMenu();
    }
}
