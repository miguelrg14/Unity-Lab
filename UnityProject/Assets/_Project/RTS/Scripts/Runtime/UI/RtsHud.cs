using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityLab.RTS.Army;
using UnityLab.RTS.Core;
using UnityLab.RTS.Units;
using UnityLab.Shared;

namespace UnityLab.RTS.UI
{
    /// <summary>
    /// Resources and army size of the player, training buttons when the base is selected and the
    /// pause/help overlay. Built from code at startup; it finds the player's army by itself.
    /// </summary>
    public class RtsHud : MonoBehaviour
    {
        ArmyController player;
        ArmyController opponent;
        TMP_Text resourcesText;
        TMP_Text armiesText;
        GameObject trainPanel;
        readonly Button[] trainButtons = new Button[3];
        readonly TMP_Text[] trainLabels = new TMP_Text[3];
        readonly StringBuilder text = new StringBuilder();

        void Start()
        {
            foreach (ArmyController army in FindObjectsByType<ArmyController>())
            {
                if (army.PlayerControlled && player == null)
                    player = army;
                else
                    opponent = army;
            }
            if (player == null)
            {
                enabled = false;
                return;
            }

            Build();
            PauseOverlay.Create("RTS skirmish",
                "Left click / drag  select units\nShift + click  add to the selection\nRight click  move, attack or harvest\n" +
                "Click your base, then A / S / D  train melee, artillery, harvester\nDelete  kill the selection\n" +
                "Arrows or screen edge  pan, wheel  zoom\nF1 unit states, F2 vision matrix\nEsc  pause");
        }

        void Build()
        {
            Canvas canvas = UiFactory.CreateCanvas("RTS HUD", 10);

            RectTransform top = UiFactory.Panel(canvas.transform, "Top Bar", UiTheme.Panel);
            UiFactory.Anchor(top, new Vector2(0f, 1f), new Vector2(560, 120), new Vector2(24, -24));
            UiFactory.Vertical(top, 6, 18);
            resourcesText = UiFactory.Label(top, string.Empty, 28, UiTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            armiesText = UiFactory.Label(top, string.Empty, 22, UiTheme.MutedText);

            RectTransform panel = UiFactory.Panel(canvas.transform, "Train Panel", UiTheme.Panel);
            UiFactory.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(980, 120), new Vector2(0, 24));
            UiFactory.Horizontal(panel, 16, 18);
            string[] names = { "Melee (A)", "Artillery (S)", "Harvester (D)" };
            for (int i = 0; i < 3; i++)
            {
                var kind = (UnitKind)i;
                trainButtons[i] = UiFactory.Button(panel, names[i], () => Train(kind));
                trainLabels[i] = trainButtons[i].GetComponentInChildren<TMP_Text>();
            }
            trainPanel = panel.gameObject;

            RectTransform hint = UiFactory.Group(canvas.transform, "Hint");
            UiFactory.Anchor(hint, new Vector2(1f, 1f), new Vector2(520, 60), new Vector2(-24, -24));
            TMP_Text hintText = UiFactory.Label(hint, "Esc  controls and menu", 22, UiTheme.MutedText, TextAlignmentOptions.TopRight);
            UiFactory.Stretch(hintText.rectTransform);
        }

        void Train(UnitKind kind)
        {
            if (player.SelectedBase != null)
                player.Train(player.SelectedBase, kind);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                Unit.ShowStateLabels = !Unit.ShowStateLabels;

            ArmyBase home = player.MainBase;
            if (home != null)
            {
                text.Clear().Append("Stone ").Append(home.Treasury.Stone).Append("     Wood ").Append(home.Treasury.Wood);
                SetText(resourcesText, text.ToString());
            }

            text.Clear().Append("Your units ").Append(player.Units.Count);
            if (opponent != null)
                text.Append("   ·   Enemy units ").Append(opponent.Units.Count);
            text.Append("   ·   Selected ").Append(player.SelectedUnits.Count);
            SetText(armiesText, text.ToString());

            ArmyBase selected = player.SelectedBase;
            trainPanel.SetActive(selected != null);
            if (selected == null)
                return;
            for (int i = 0; i < 3; i++)
            {
                var kind = (UnitKind)i;
                trainButtons[i].interactable = selected.CanAfford(kind);
                string label = $"{(kind == UnitKind.Melee ? "Melee (A)" : kind == UnitKind.Artillery ? "Artillery (S)" : "Harvester (D)")}\n<size=70%>{selected.CostOf(kind)}</size>";
                SetText(trainLabels[i], label);
            }
        }

        static void SetText(TMP_Text label, string value)
        {
            if (label.text != value)
                label.text = value;
        }
    }
}
