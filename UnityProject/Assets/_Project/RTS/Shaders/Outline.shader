// Selection outline for the RTS units and buildings, used as the renderer's second material.
// It draws the back faces of the mesh pushed out along their normals, so only a rim around the
// object is visible. _Outline_Width 0 hides it; Selectable.cs animates both properties per
// renderer through a MaterialPropertyBlock.
Shader "UnityLab/Outline"
{
    Properties
    {
        _Outline_Color ("Outline Color", Color) = (1, 1, 1, 1)
        _Outline_Width ("Outline Width", Range(0, 0.3)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Outline_Color;
            float _Outline_Width;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // A width of 0 collapses the shell onto the mesh, where the depth test hides it.
                float3 expanded = v.vertex.xyz + normalize(v.normal) * _Outline_Width;
                o.position = UnityObjectToClipPos(float4(expanded, 1));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(_Outline_Width - 0.0001);
                return _Outline_Color;
            }
            ENDCG
        }
    }
}
