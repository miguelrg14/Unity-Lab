using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityLab.Shared;

namespace UnityLab.Menu
{
    /// <summary>
    /// Entry screen: one card per demo. The online card collects the nickname and the server
    /// address, which <see cref="GameSession"/> remembers for next time.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        TMP_InputField nicknameField;
        TMP_InputField hostField;
        TMP_InputField portField;
        TMP_Text onlineError;

        void Start()
        {
            Time.timeScale = 1f;
            Canvas canvas = UiFactory.CreateCanvas("Main Menu");
            RectTransform background = UiFactory.Panel(canvas.transform, "Background", UiTheme.Background);
            UiFactory.Stretch(background);

            RectTransform page = UiFactory.Group(background, "Page");
            UiFactory.Stretch(page, 120, 80);
            VerticalLayoutGroup pageLayout = UiFactory.Vertical(page, 28, 0);
            pageLayout.childForceExpandHeight = false;

            UiFactory.Label(page, "Unity Lab", 96, UiTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiFactory.Label(page, "Gameplay programming prototypes: physics racing with AI and online multiplayer, and a real-time strategy skirmish.", 30, UiTheme.MutedText);

            RectTransform cards = UiFactory.Group(page, "Cards");
            HorizontalLayoutGroup row = UiFactory.Horizontal(cards, 32, 0);
            row.childForceExpandHeight = true;
            UiFactory.Size(cards, preferredHeight: 600);

            BuildCard(cards, "Racing", "vs AI",
                "Physics car on WheelColliders against three AI drivers that follow the racing line and dodge obstacles. Two laps.",
                "Race", () => SceneNavigator.Load(Scenes.RacingSinglePlayer));
            BuildOnlineCard(cards);
            BuildCard(cards, "RTS", "Skirmish",
                "Train units, harvest stone and wood and fight an AI commander. Units react to enemies through a shared vision matrix.",
                "Play", () => SceneNavigator.Load(Scenes.RtsBattlefield));

            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                Button quit = UiFactory.Button(page, "Quit", Application.Quit, primary: false);
                UiFactory.Size(quit, preferredHeight: 56, flexibleWidth: 0, preferredWidth: 220);
            }
        }

        static RectTransform Card(Transform parent, string title, string subtitle, string description)
        {
            RectTransform card = UiFactory.Panel(parent, "Card " + title, UiTheme.Panel);
            UiFactory.Vertical(card, 16, 36);
            UiFactory.Label(card, title, 52, UiTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiFactory.Label(card, subtitle.ToUpperInvariant(), 24, UiTheme.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            TMP_Text body = UiFactory.Label(card, description, 26, UiTheme.MutedText);
            UiFactory.Size(body, preferredHeight: 150);
            return card;
        }

        static void BuildCard(Transform parent, string title, string subtitle, string description, string action, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform card = Card(parent, title, subtitle, description);
            RectTransform spacer = UiFactory.Group(card, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            UiFactory.Button(card, action, onClick);
        }

        void BuildOnlineCard(Transform parent)
        {
            RectTransform card = Card(parent, "Racing", "Online",
                "Race other players through the dedicated server (run it with Docker Compose). Up to four cars.");

            if (!GameSession.SupportsOnlinePlay)
            {
                UiFactory.Label(card, "Online races need the desktop build: browsers cannot open TCP connections.", 24, UiTheme.Danger);
                return;
            }

            nicknameField = UiFactory.InputField(card, "Nickname", GameSession.Nickname, characterLimit: 16);
            RectTransform address = UiFactory.Group(card, "Address");
            UiFactory.Horizontal(address, 12, 0);
            hostField = UiFactory.InputField(address, "Server", GameSession.ServerHost, characterLimit: 64);
            UiFactory.Size(hostField, flexibleWidth: 3);
            portField = UiFactory.InputField(address, "Port", GameSession.ServerPort.ToString(), TMP_InputField.ContentType.IntegerNumber, 5);
            UiFactory.Size(portField, flexibleWidth: 1);
            onlineError = UiFactory.Label(card, string.Empty, 22, UiTheme.Danger);
            UiFactory.Button(card, "Connect", Connect);
        }

        void Connect()
        {
            string host = hostField.text.Trim();
            if (host.Length == 0 || !int.TryParse(portField.text, out int port) || port < 1 || port > 65535)
            {
                onlineError.text = "Enter a server address and a port between 1 and 65535.";
                return;
            }

            GameSession.Nickname = string.IsNullOrWhiteSpace(nicknameField.text) ? "Player" : nicknameField.text.Trim();
            GameSession.ServerHost = host;
            GameSession.ServerPort = port;
            GameSession.Save();
            SceneNavigator.Load(Scenes.RacingOnline);
        }
    }
}
