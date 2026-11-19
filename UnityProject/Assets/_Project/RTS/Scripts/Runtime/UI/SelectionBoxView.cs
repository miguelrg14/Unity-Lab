using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.RTS.Army;

namespace UnityLab.RTS.UI
{
    /// <summary>Draws the rectangle the player is dragging to select units.</summary>
    [RequireComponent(typeof(Canvas))]
    public class SelectionBoxView : MonoBehaviour
    {
        [SerializeField] ArmyController army;
        [FormerlySerializedAs("selectionImage")]
        [SerializeField] RectTransform box;

        Canvas canvas;

        public ArmyController Army
        {
            get => army;
            set => army = value;
        }

        void Awake() => canvas = GetComponent<Canvas>();

        void Start() => box.gameObject.SetActive(false);

        void Update()
        {
            bool visible = army != null && army.IsBoxSelecting;
            if (box.gameObject.activeSelf != visible)
                box.gameObject.SetActive(visible);
            if (!visible)
                return;

            float scale = canvas.scaleFactor;
            box.anchoredPosition = new Vector2(army.CurrentBox.XMin / scale, army.CurrentBox.YMin / scale);
            box.sizeDelta = new Vector2(army.CurrentBox.Width / scale, army.CurrentBox.Height / scale);
        }
    }
}
