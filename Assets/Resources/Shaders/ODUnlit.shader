// Orbital Descent - flat colour (parachute risers, debug lines).
Shader "OrbitalDescent/Unlit"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };
            v2f vert(appdata_full v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }
            float4 frag(v2f i) : SV_Target { return _Color * i.color; }
            ENDCG
        }
    }
    Fallback Off
}
