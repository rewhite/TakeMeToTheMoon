Shader "TakeMeToTheMoon/Video Floor Glow"
{
    Properties
    {
        _MainTex ("Blurred Video", 2D) = "black" {}
        _Intensity ("Intensity", Range(0, 2)) = 0.55
        _FlipVertical ("Flip Vertical", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "FloorGlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ColorMask RGB
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Intensity;
                float _FlipVertical;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                // Smoothly disappear at every edge. V=0 is nearest to the screen.
                float side = saturate(1.0 - pow(abs(uv.x * 2.0 - 1.0), 2.0));
                float fade = side * side * smoothstep(0.0, 0.16, uv.y) * pow(1.0 - uv.y, 2.0);
                float2 videoUV = float2(uv.x, lerp(uv.y, 1.0 - uv.y, _FlipVertical));
                half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, videoUV).rgb;
                return half4(color, fade * _Intensity);
            }
            ENDHLSL
        }
    }
}
