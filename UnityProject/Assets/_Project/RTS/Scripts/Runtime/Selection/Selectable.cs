using UnityEngine;

namespace UnityLab.RTS.Selection
{
    /// <summary>
    /// Shows team colour and selection on a renderer whose second material uses the outline shader.
    /// Uses a MaterialPropertyBlock, so no material instances are created (and none can leak).
    /// The first material is tinted with the team colour so armies are easy to tell apart.
    /// </summary>
    public class Selectable : MonoBehaviour
    {
        const float SelectedOutlineWidth = 0.08f;
        static readonly int OutlineWidth = Shader.PropertyToID("_Outline_Width");
        static readonly int OutlineColor = Shader.PropertyToID("_Outline_Color");
        static readonly int BaseColor = Shader.PropertyToID("_Color");

        Renderer targetRenderer;
        MaterialPropertyBlock block;
        Color teamColor = Color.white;

        public bool IsSelected { get; private set; }

        void Awake()
        {
            targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            Apply();
        }

        public void SetTeamColor(Color color)
        {
            teamColor = color;
            Apply();
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            Apply();
        }

        void Apply()
        {
            if (targetRenderer == null)
                return;

            // Body: a light tint of the team colour.
            block.Clear();
            block.SetColor(BaseColor, Color.Lerp(Color.white, teamColor, 0.55f));
            targetRenderer.SetPropertyBlock(block, 0);

            if (targetRenderer.sharedMaterials.Length < 2)
                return;
            block.Clear();
            block.SetColor(OutlineColor, teamColor);
            block.SetFloat(OutlineWidth, IsSelected ? SelectedOutlineWidth : 0f);
            targetRenderer.SetPropertyBlock(block, 1);
        }
    }
}
