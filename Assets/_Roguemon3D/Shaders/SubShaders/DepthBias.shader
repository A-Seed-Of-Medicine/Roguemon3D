Shader "Custom/GroundLitDepthBiasedURP"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)

        // Small positive value = bias depth away from camera (farther)
        // Keep this very small: 0.0 - 0.002 is a sensible range.
        _DepthBias("Depth Bias", Range(-0.01, 0.01)) = 0.001
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // URP core + lighting
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DepthBias;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.positionWS  = positionWS;
                OUT.normalWS    = normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);

                // Shadow coordinates for main light
                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);

                return OUT;
            }

            // Explicit color + depth so we can bias depth
            struct FragOutput
            {
                float4 color : SV_Target;
                float  depth : SV_Depth;
            };

            FragOutput frag (Varyings IN)
            {
                FragOutput o;

                // Normalize inputs
                float3 N = normalize(IN.normalWS);

                // Sample base color
                float4 albedoTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 albedo    = albedoTex.rgb * _BaseColor.rgb;
                float  alpha     = albedoTex.a * _BaseColor.a;

                // Main directional light with shadows
                Light mainLight = GetMainLight(IN.shadowCoord);

                float3 L = normalize(mainLight.direction);
                float  NdotL = saturate(dot(N, L));

                // Diffuse from main light, with shadow + distance attenuation
                float3 diffuse = albedo * (NdotL * mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation);

                // Ambient / probes (SH)
                float3 ambient = albedo * SampleSH(N);

                float3 finalColor = diffuse + ambient;

                o.color = float4(finalColor, alpha);

                // Default depth from clip space
                float ndcDepth = IN.positionHCS.z / IN.positionHCS.w;

                // Push depth away from camera: positive bias = farther
                // Keep _DepthBias small, e.g. 0.0005–0.002
                o.depth = saturate(ndcDepth + _DepthBias);

                return o;
            }

            ENDHLSL
        }
    }
}
