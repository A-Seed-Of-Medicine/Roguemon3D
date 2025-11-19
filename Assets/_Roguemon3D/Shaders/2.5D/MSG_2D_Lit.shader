Shader "Universal Render Pipeline/Sprites/SpriteLit3D_TwoSided_GPU"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color   ("Tint", Color)        = (1,1,1,1)

        _Smoothness ("Smoothness", Range(0,1)) = 0.4
        _SpecColor  ("Specular Color", Color)  = (1,1,1,1)

        // These match what SpriteRenderer normally drives
        [HideInInspector] _RendererColor       ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex            ("External Alpha", 2D)   = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"              = "Transparent"
            "RenderType"         = "Transparent"
            "RenderPipeline"     = "UniversalPipeline"
            "IgnoreProjector"    = "True"
            "CanUseSpriteAtlas"  = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off          // draw both sides
        ZWrite Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            // URP lighting feature variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // Fog and instancing
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // GPU sprite skinning (SpriteSkin)
            #pragma multi_compile _ SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _RendererColor;
                float  _Smoothness;
                float4 _SpecColor;
                float  _EnableExternalAlpha;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;

                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 worldPos   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Sprite sampling with optional external alpha
            inline float4 SampleSprite(float2 uv)
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                if (_EnableExternalAlpha > 0.5)
                {
                    float a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, uv).r;
                    c.a *= a;
                }

                return c;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Apply GPU bone deformation for SpriteSkin
                UNITY_SKINNED_VERTEX_COMPUTE(IN);

                float3 posOS = IN.positionOS;

                // Respect SpriteRenderer flip (X/Y)
                posOS = UnityFlipSprite(posOS, unity_SpriteProps.xy);

                float3 posWS = TransformObjectToWorld(posOS);

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.worldPos   = posWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);

                // Treat the sprite as facing the camera; we'll do two-sided lighting in frag
                OUT.normalWS   = -GetViewForwardDir();

                // Vertex color * material tint * per-renderer tint * sprite color
                OUT.color = IN.color * _Color * _RendererColor * unity_SpriteColor;

                return OUT;
            }

            // Helper: accumulate diffuse + specular for a single light, using two-sided normals
            inline void ApplyLight(
                Light  light,
                half3  N,
                half3  V,
                half   smoothness,
                half3  specColor,
                inout half3 diffAccum,
                inout half3 specAccum)
            {
                half3 L = light.direction;

                // Two-sided: use abs(dot) so back faces get lit too
                half  NdotL = saturate(abs(dot(N, L)));
                half3 lightCol = light.color *
                                 (light.distanceAttenuation * light.shadowAttenuation);

                half3 diff = NdotL * lightCol;

                half3 H = SafeNormalize(L + V);
                half  NdotH = saturate(abs(dot(N, H)));

                // Very simple gloss mapping
                half  specPower  = exp2(smoothness * 10.0h + 1.0h);
                half  specFactor = pow(NdotH, specPower);
                half3 spec       = specFactor * NdotL * lightCol * specColor;

                diffAccum += diff;
                specAccum += spec;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = (half4)SampleSprite(IN.uv);
                half4 baseColor = tex * IN.color;

                // Kill fully transparent fragments
                clip(baseColor.a - 0.001h);

                half3 N = normalize(IN.normalWS);
                half3 V = SafeNormalize(GetWorldSpaceViewDir(IN.worldPos));

                // Main light (with shadows if enabled in URP)
                float4 shadowCoord = TransformWorldToShadowCoord(IN.worldPos);
                Light mainLight    = GetMainLight(shadowCoord);

                half3 totalDiffuse = 0;
                half3 totalSpec    = 0;

                ApplyLight(mainLight, N, V, (half)_Smoothness, (half3)_SpecColor.rgb,
                           totalDiffuse, totalSpec);

            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                [loop]
                for (uint i = 0u; i < count; i++)
                {
                    Light l = GetAdditionalLight(i, IN.worldPos);
                    ApplyLight(l, N, V, (half)_Smoothness, (half3)_SpecColor.rgb,
                               totalDiffuse, totalSpec);
                }
            #endif

                half3 lighting = totalDiffuse + totalSpec;

                // Small ambient floor so things are never totally black
                lighting += 0.1h;

                half3 finalRGB = baseColor.rgb * lighting;

                return half4(finalRGB, baseColor.a);
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
