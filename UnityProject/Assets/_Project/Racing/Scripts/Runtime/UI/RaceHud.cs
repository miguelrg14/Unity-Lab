using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.Racing.Cars;
using UnityLab.Racing.Core;
using UnityLab.Racing.Race;
using UnityLab.Shared;

namespace UnityLab.Racing.UI
{
    /// <summary>
    /// In-race HUD: speedometer, the player's position and lap, the leaderboard and, when the
    /// player crosses the finish line, a results panel.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [FormerlySerializedAs("circuitMng")]
        [SerializeField] CircuitManager circuit;
        [Tooltip("The local player's car.")]
        [FormerlySerializedAs("car")]
        [SerializeField] CarController playerCar;

        [Header("Texts")]
        [FormerlySerializedAs("velocimeter")]
        [SerializeField] TMP_Text speedText;
        [SerializeField] TMP_Text positionText;
        [FormerlySerializedAs("maxCompetitors_txt")]
        [SerializeField] TMP_Text competitorCountText;
        [SerializeField] TMP_Text lapText;
        [SerializeField] TMP_Text totalLapsText;

        [Header("Leaderboard")]
        [Tooltip("Parent of one row per position; each row has a name text and a position text.")]
        [FormerlySerializedAs("summaryBoard_Canvas")]
        [SerializeField] GameObject leaderboard;

        Competitor player;
        GameObject resultsPanel;
        TMP_Text resultsText;

        public CarController PlayerCar
        {
            get => playerCar;
            set
            {
                playerCar = value;
                player = value != null ? value.GetComponent<Competitor>() : null;
            }
        }

        void Awake()
        {
            PlayerCar = playerCar;
            BuildResultsPanel();
        }

        void OnEnable()
        {
            if (circuit == null)
                return;
            circuit.StandingsChanged += RefreshStandings;
            circuit.CompetitorFinished += OnCompetitorFinished;
        }

        void OnDisable()
        {
            if (circuit == null)
                return;
            circuit.StandingsChanged -= RefreshStandings;
            circuit.CompetitorFinished -= OnCompetitorFinished;
        }

        void Start()
        {
            PauseOverlay.Create("Racing", "W / S  accelerate and reverse\nA / D  steer\nSpace  brake\nR  reset the car on the track\nG  driver / chase camera\nEsc  pause");
        }

        void Update()
        {
            if (playerCar != null && speedText != null)
                speedText.text = Mathf.RoundToInt(playerCar.Speed * 3.6f).ToString();
        }

        void RefreshStandings()
        {
            if (player != null && player.Progress != null)
            {
                SetText(positionText, player.Position.ToString());
                SetText(lapText, player.Progress.CurrentLap.ToString());
            }
            SetText(competitorCountText, circuit.Standings.Count.ToString());
            SetText(totalLapsText, circuit.Laps.ToString());

            if (leaderboard == null)
                return;
            Transform rows = leaderboard.transform;
            for (int i = 0; i < rows.childCount; i++)
            {
                Transform row = rows.GetChild(i);
                bool used = i < circuit.Standings.Count;
                row.gameObject.SetActive(used);
                if (!used || row.childCount < 2)
                    continue;

                Competitor competitor = circuit.Standings[i];
                TMP_Text nameText = row.GetChild(0).GetComponent<TMP_Text>();
                TMP_Text placeText = row.GetChild(1).GetComponent<TMP_Text>();
                SetText(nameText, competitor.Nickname);
                SetText(placeText, RaceStandings.Ordinal(i + 1));
                // The player's own row stands out.
                Color color = competitor.IsLocalPlayer ? UiTheme.Accent : Color.white;
                if (nameText != null)
                    nameText.color = color;
                if (placeText != null)
                    placeText.color = color;
            }
        }

        void OnCompetitorFinished(Competitor competitor)
        {
            if (competitor != player)
                return;
            resultsText.text = $"You finished {RaceStandings.Ordinal(player.Position)}";
            resultsPanel.SetActive(true);
        }

        void BuildResultsPanel()
        {
            Canvas canvas = UiFactory.CreateCanvas("Results", 50);
            RectTransform card = UiFactory.Panel(canvas.transform, "Results Card", UiTheme.Panel);
            UiFactory.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(620, 300), Vector2.zero);
            UiFactory.Vertical(card, 20, 40);
            resultsText = UiFactory.Label(card, string.Empty, 52, UiTheme.Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Button(card, "Race again", () => SceneNavigator.Load(gameObject.scene.name));
            UiFactory.Button(card, "Back to menu", SceneNavigator.BackToMenu, primary: false);
            resultsPanel = canvas.gameObject;
            resultsPanel.SetActive(false);
        }

        static void SetText(TMP_Text text, string value)
        {
            if (text != null && text.text != value)
                text.text = value;
        }
    }
}
