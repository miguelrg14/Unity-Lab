using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityLab.Shared;

namespace UnityLab.Tests.PlayMode
{
    public class MenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator MainMenu_OffersEveryDemo()
        {
            yield return SceneManager.LoadSceneAsync(Scenes.MainMenu);
            yield return null;

            string[] labels = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .Select(b => b.GetComponentInChildren<TMP_Text>().text)
                .ToArray();
            Assert.That(labels, Does.Contain("Race"));
            Assert.That(labels, Does.Contain("Connect"));
            Assert.That(labels, Does.Contain("Play"));
        }

        [UnityTest]
        public IEnumerator PlayButton_OpensTheRtsScene()
        {
            yield return SceneManager.LoadSceneAsync(Scenes.MainMenu);
            yield return null;

            Button play = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .First(b => b.GetComponentInChildren<TMP_Text>().text == "Play");
            play.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(Scenes.RtsBattlefield));
        }

        [UnityTest]
        public IEnumerator Escape_PausesTheGameAndResumes()
        {
            yield return SceneManager.LoadSceneAsync(Scenes.RtsBattlefield);
            yield return null;
            PauseOverlay overlay = Object.FindAnyObjectByType<PauseOverlay>();
            Assert.That(overlay, Is.Not.Null);

            overlay.Toggle();
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            overlay.Toggle();
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }
    }
}
