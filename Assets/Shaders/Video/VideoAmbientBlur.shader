Shader "Hidden/TakeMeToTheMoon/Video Ambient Blur"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _Direction;
        half4 Downsample(v2f_img i) : SV_Target
        {
            float2 d = float2(1.0 / 128.0, 1.0 / 72.0);
            return (tex2D(_MainTex, saturate(i.uv + d))
                + tex2D(_MainTex, saturate(i.uv - d))
                + tex2D(_MainTex, saturate(i.uv + float2(d.x, -d.y)))
                + tex2D(_MainTex, saturate(i.uv + float2(-d.x, d.y)))) * .25;
        }
        half4 Blur(v2f_img i) : SV_Target
        {
            float2 d = _Direction.xy * abs(_MainTex_TexelSize.xy);
            half4 color = tex2D(_MainTex, saturate(i.uv)) * .227027;
            color += (tex2D(_MainTex, saturate(i.uv + d)) + tex2D(_MainTex, saturate(i.uv - d))) * .1945946;
            color += (tex2D(_MainTex, saturate(i.uv + d * 2)) + tex2D(_MainTex, saturate(i.uv - d * 2))) * .1216216;
            color += (tex2D(_MainTex, saturate(i.uv + d * 3)) + tex2D(_MainTex, saturate(i.uv - d * 3))) * .054054;
            color += (tex2D(_MainTex, saturate(i.uv + d * 4)) + tex2D(_MainTex, saturate(i.uv - d * 4))) * .016216;
            return color;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Downsample
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Blur
            ENDCG
        }
    }
}
