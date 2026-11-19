using TMPro;
using UnityEngine;

namespace UnityLab.Shared
{
    /// <summary>
    /// Escape opens a small panel with the controls of the current demo and a way back to the menu.
    /// Every demo scene creates one through <see cref="Create"/>.
    /// </summary>
    public sealed class PauseOverlay : MonoBehaviour
    {
        GameObject panel;
        float previousTimeScale = 1f;

        public bool IsOpen => panel != null && panel.activeSelf;

        public static PauseOverlay Create(string title, string controls)
        {
            Canvas canvas = UiFactory.CreateCanvas("Pause Overlay", 100);
            var overlay = canvas.gameObject.AddComponent<PauseOverlay>();
            overlay.Build(canvas.transform, title, controls);
            return overlay;
        }

        void Build(Transform root, string title, string controls)
        {
            RectTransform dim = UiFactory.Panel(root, "Dim", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(dim);
            panel = dim.gameObject;

            RectTransform card = UiFactory.Panel(dim, "Card", UiTheme.Panel);
            UiFactory.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(720, 0), Vector2.zero);
            card.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            UiFactory.Vertical(card, 18, 40);

            UiFactory.Label(card, title, 44, UiTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiFactory.Label(card, controls, 24, UiTheme.MutedText);
            UiFactory.Button(card, "Resume", Toggle);
            UiFactory.Button(card, "Back to menu", () =>
            {
                Time.timeScale = 1f;
                SceneNavigator.BackToMenu();
            }, primary: false);

            panel.SetActive(false);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                Toggle();
        }

        public void Toggle()
        {
            bool open = !panel.activeSelf;
            panel.SetActive(open);
            if (open)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = previousTimeScale;
            }
        }

        void OnDestroy()
        {
            if (IsOpen)
                Time.timeScale = previousTimeScale;
        }
    }
}
