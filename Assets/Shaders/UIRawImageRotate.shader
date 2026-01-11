Shader "StargazerProbe/UI/RawImageRotate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RotationDegrees ("Rotation (Degrees)", Float) = 0
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _RotationDegrees;
            float _FlipY;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            float2 RotateUV(float2 uv, float degrees)
            {
                float rad = degrees * 0.017453292519943295; // PI/180
                float s = sin(rad);
                float c = cos(rad);

                // rotate around center
                uv -= 0.5;
                float2 r;
                r.x = uv.x * c - uv.y * s;
                r.y = uv.x * s + uv.y * c;
                r += 0.5;
                return r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                if (_FlipY > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }

                uv = RotateUV(uv, _RotationDegrees);

                fixed4 col = tex2D(_MainTex, uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
}
