// LR2: minimal HDRP-compatible unlit particle shader (vertex color aware, additive or alpha blended).
// Built-in particle shaders render pink under HDRP; this keeps muzzle flashes, sparks, dust, blood and tracers working.
Shader "LR2/ParticleUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Toggle] _PremultiplyAlpha ("Premultiply alpha into color (for additive)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Intensity;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
                float _PremultiplyAlpha;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(positionRWS);
                o.color = input.color;
                o.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float4 c = tex * i.color * _Color;
                c.rgb *= _Intensity;
                if (_PremultiplyAlpha > 0.5) c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
