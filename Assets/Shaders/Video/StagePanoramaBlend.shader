Shader "TakeMeToTheMoon/Stage Panorama Blend"
{
    Properties
    {
        _MainTex ("Previous panorama", 2D) = "grey" {}
        _NextTex ("Next panorama", 2D) = "grey" {}
        _Rotation ("Previous rotation", Float) = 0
        _NextRotation ("Next rotation", Float) = 0
        _Exposure ("Previous exposure", Float) = 1
        _NextExposure ("Next exposure", Float) = 1
        _Blend ("Blend", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex, _NextTex;
            half4 _MainTex_HDR, _NextTex_HDR;
            float _Rotation, _NextRotation, _Exposure, _NextExposure, _Blend;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            float2 PanoramaUV(float3 direction, float rotation)
            {
                float3 d = normalize(direction);
                float longitude = atan2(d.z, d.x) + radians(rotation);
                return float2(0.5 - longitude / (2 * UNITY_PI), 1 - acos(clamp(d.y, -1, 1)) / UNITY_PI);
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 a = DecodeHDR(tex2D(_MainTex, PanoramaUV(i.direction, _Rotation)), _MainTex_HDR);
                half3 b = DecodeHDR(tex2D(_NextTex, PanoramaUV(i.direction, _NextRotation)), _NextTex_HDR);
                // Match Skybox/Panoramic's default 0.5 tint and color-space multiplier.
                return half4(lerp(a * _Exposure, b * _NextExposure, _Blend) * (0.5 * unity_ColorSpaceDouble.rgb), 1);
            }
            ENDCG
        }
    }
}
