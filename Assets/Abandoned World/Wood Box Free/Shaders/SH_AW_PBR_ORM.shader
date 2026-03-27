// Converti de Built-in Pipeline (CGPROGRAM/Surface Shader) vers URP 17 (HLSLPROGRAM).
// Fonctionnalités identiques à l'original :
//   Albedo (_MainTex * _Color) + Normal map + ORM (R=AO, G=Roughness, B=Metallic) + Emissive.
// Compatible SRP Batcher, GPU Instancing, Stereo Rendering (VR).

Shader "SH_AW_PBR_ORM"
{
    Properties
    {
        _Color          ("Color",               Color)         = (1,1,1,1)
        _MainTex        ("Albedo (RGB)",         2D)            = "white"  {}
        _Normal         ("Normal Map",           2D)            = "bump"   {}
        _ORM            ("AO,Roughness,Metallic",2D)            = "white"  {}
        _Emissive       ("Emissive",             2D)            = "black"  {}
        _NormalStrength ("Normal Map Strength",  Float)         = 1.0
        _Roughness      ("Roughness",            Range(0,2))    = 1
        _Metallic       ("Metallic",             Range(0,2))    = 1
        _AO_Intensity   ("AO Intensity",         Range(0,10))   = 1
        _Emission       ("Emission",             Float)         = 0
        _ColorEmission  ("Color Emissive",       Color)         = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 200

        // ══════════════════════════════════════════════════════════════════════
        //  PASS 1 — ForwardLit (éclairage PBR complet)
        // ══════════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // Ombres + lumières additionnelles
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Textures ───────────────────────────────────────────────────────
            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_Normal);    SAMPLER(sampler_Normal);
            TEXTURE2D(_ORM);       SAMPLER(sampler_ORM);
            TEXTURE2D(_Emissive);  SAMPLER(sampler_Emissive);

            // ── Constantes (SRP Batcher : toutes les propriétés dans ce bloc) ─
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Normal_ST;
                float4 _ORM_ST;
                float4 _Emissive_ST;
                half4  _Color;
                half4  _ColorEmission;
                half   _NormalStrength;
                half   _Roughness;
                half   _Metallic;
                half   _AO_Intensity;
                half   _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float4 tangentOS   : TANGENT;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uvMain       : TEXCOORD0;
                float2 uvNormal     : TEXCOORD1;
                float2 uvORM        : TEXCOORD2;
                float2 uvEmissive   : TEXCOORD3;
                float3 positionWS   : TEXCOORD4;
                float3 normalWS     : TEXCOORD5;
                float3 tangentWS    : TEXCOORD6;
                float3 bitangentWS  : TEXCOORD7;
                float  fogFactor    : TEXCOORD8;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS  = posInputs.positionCS;
                OUT.positionWS   = posInputs.positionWS;
                OUT.normalWS     = normInputs.normalWS;
                OUT.tangentWS    = normInputs.tangentWS;
                OUT.bitangentWS  = normInputs.bitangentWS;
                OUT.uvMain       = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.uvNormal     = TRANSFORM_TEX(IN.uv, _Normal);
                OUT.uvORM        = TRANSFORM_TEX(IN.uv, _ORM);
                OUT.uvEmissive   = TRANSFORM_TEX(IN.uv, _Emissive);
                OUT.fogFactor    = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ── Albedo ────────────────────────────────────────────────────
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uvMain) * _Color;

                // ── Normal map ────────────────────────────────────────────────
                // UnpackNormal → tangent space [-1,1]. On scale XY par _NormalStrength
                // pour contrôler l'intensité (même comportement que l'original).
                half4 normalSample = SAMPLE_TEXTURE2D(_Normal, sampler_Normal, IN.uvNormal);
                half3 normalTS     = UnpackNormal(normalSample);
                normalTS.xy       *= _NormalStrength;
                normalTS           = normalize(normalTS);

                // Tangent → World
                float3x3 TBN    = float3x3(normalize(IN.tangentWS),
                                           normalize(IN.bitangentWS),
                                           normalize(IN.normalWS));
                float3 normalWS = normalize(mul(normalTS, TBN));

                // ── ORM (R=AO, G=Roughness, B=Metallic) ──────────────────────
                half4 orm       = SAMPLE_TEXTURE2D(_ORM, sampler_ORM, IN.uvORM);
                half metallic   = saturate(orm.b * _Metallic);
                // Roughness → Smoothness : 1-roughness (même formule que l'original)
                half smoothness = saturate((1.0h - orm.g) * _Roughness);
                half ao         = saturate(orm.r * _AO_Intensity);

                // ── Emissive ──────────────────────────────────────────────────
                half4 emissiveTex = SAMPLE_TEXTURE2D(_Emissive, sampler_Emissive, IN.uvEmissive);
                half3 emission    = emissiveTex.rgb * _Emission * _ColorEmission.rgb;

                // ── PBR InputData ─────────────────────────────────────────────
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                = IN.fogFactor;
                inputData.vertexLighting          = half3(0, 0, 0);
                // GI modulé par AO pour conserver l'occlusion ambiante
                inputData.bakedGI                 = SampleSH(normalWS) * ao;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                // ── PBR SurfaceData ───────────────────────────────────────────
                SurfaceData surfData = (SurfaceData)0;
                surfData.albedo     = albedo.rgb;
                surfData.alpha      = albedo.a;
                surfData.metallic   = metallic;
                surfData.smoothness = smoothness;
                surfData.normalTS   = normalTS;
                surfData.occlusion  = ao;
                surfData.emission   = emission;
                surfData.specular   = half3(0, 0, 0);

                half4 color = UniversalFragmentPBR(inputData, surfData);
                color.rgb   = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PASS 2 — ShadowCaster
        // ══════════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Normal_ST;
                float4 _ORM_ST;
                float4 _Emissive_ST;
                half4  _Color;
                half4  _ColorEmission;
                half   _NormalStrength;
                half   _Roughness;
                half   _Metallic;
                half   _AO_Intensity;
                half   _Emission;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetShadowClipPos(ShadowAttributes input)
            {
                float3 posWS  = TransformObjectToWorld(input.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return posCS;
            }

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = GetShadowClipPos(IN);
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PASS 3 — DepthOnly (requis pour SSAO et effets post-process URP)
        // ══════════════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Normal_ST;
                float4 _ORM_ST;
                float4 _Emissive_ST;
                half4  _Color;
                half4  _ColorEmission;
                half   _NormalStrength;
                half   _Roughness;
                half   _Metallic;
                half   _AO_Intensity;
                half   _Emission;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
