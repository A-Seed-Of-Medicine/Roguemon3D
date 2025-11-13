Shader "Custom/URP/SpriteLitDoubleSidedGPU"
{
    Properties
    {
        [MainTexture] _MainTex("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color("Color", Color)        = (1,1,1,1)

        _Smoothness("Smoothness", Range(0,1))       = 0.5
        _SpecColor("Specular Color", Color)         = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Standard transparent sprite settings
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off                // draw both sides of the quad
        ZWrite Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            // URP lighting feature variants (same family as URP/Lit)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            // GPU Sprite Skinning
            #pragma multi_compile _ SKINNED_SPRITE

            // Includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // NOTE: Keep material properties in a single CBUFFER for SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Smoothness;
                float4 _SpecColor;
            CBUFFER_END

            // Per-vertex data from Sprite mesh
            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;

                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Data interpolated to the fragment shader
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Apply GPU sprite deformation (SpriteSkin)
                UNITY_SKINNED_VERTEX_COMPUTE(v);

                // Handle sprite flipping (SpriteRenderer flip X/Y etc.)
                float3 posOS = v.positionOS;
                posOS = UnityFlipSprite(posOS, unity_SpriteProps.xy);

                // Transform to world and clip space
                o.positionWS = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);

                o.uv    = v.uv;
                o.color = v.color;

                // Sprite quad normal in object space is typically (0,0,-1).
                // Use that and transform to world space. This lets the sprite
                // rotate in 3D (2.5D) while still shading correctly.
                float3 normalOS = float3(0.0, 0.0, -1.0);
                o.normalWS = TransformObjectToWorldDir(normalOS);

                return o;
            }

            // Simple helper: two-sided lambert + spec for one light
            void AccumulateLight(
                in Light  light,
                in float3 N,
                in float3 V,
                in float3 specColor,
                in float  smoothness,
                inout float3 outDiffuse,
                inout float3 outSpecular)
            {
                // Effective radiance for this light
                float3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);

                // Two-sided Lambert: use abs(dot) so backfaces are lit the same.
                float NdotL = saturate(abs(dot(N, light.direction)));
                outDiffuse += radiance * NdotL;

                // Simple Blinn-Phong specular, also two-sided
                float3 H      = SafeNormalize(light.direction + V);
                float  NdotH  = saturate(abs(dot(N, H)));
                float  power  = lerp(8.0, 128.0, saturate(smoothness)); // map [0,1] -> [8,128]
                float  specF  = pow(NdotH, power);

                outSpecular += radiance * specColor * specF;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Sample sprite texture
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Combine vertex color, material color and SpriteRenderer color
                // unity_SpriteColor is provided by Core2D.hlsl for SpriteRenderer.
                float4 tint   = i.color * _Color * unity_SpriteColor;
                float4 albedo = tex * tint;

                float alpha = albedo.a;
                if (alpha <= 0.0001)
                    discard;

                // World normal – normalized, used for all lighting
                float3 N = normalize(i.normalWS);

                // View direction (from world position toward camera)
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                float3 diffuse  = 0.0;
                float3 specular = 0.0;

                // === Main directional light ===
                {
                    Light mainLight = GetMainLight();
                    AccumulateLight(mainLight, N, V, _SpecColor.rgb, _Smoothness, diffuse, specular);
                }

                // === Additional lights (per-pixel) ===
            #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light addLight = GetAdditionalLight(li, i.positionWS);
                    AccumulateLight(addLight, N, V, _SpecColor.rgb, _Smoothness, diffuse, specular);
                }
            #endif

                // Basic ambient from spherical harmonics / light probes
                float3 ambient = SampleSH(N);

                float3 lighting = ambient + diffuse;
                float3 color    = albedo.rgb * lighting + specular;

                return float4(color, alpha);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
