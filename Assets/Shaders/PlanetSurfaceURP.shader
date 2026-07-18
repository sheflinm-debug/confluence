Shader "Custom/PlanetSurfaceURP"
{
    // Planet terrain shader.
    // Blends between a cool/barren base color and a warm/life-rich color as
    // _LifeFraction increases (set by EraManager as life spreads).
    // _EraColor shifts the overall hue for the three era mood arcs.
    Properties
    {
        _BaseColor      ("Barren Color",    Color) = (0.4, 0.35, 0.28, 1)
        _LifeColor      ("Life Color",      Color) = (0.2, 0.55, 0.25, 1)
        _LifeFraction   ("Life Fraction",   Range(0,1)) = 0.0
        _EraColor       ("Era Tint",        Color) = (1, 1, 1, 1)
        _Smoothness     ("Smoothness",      Range(0,1)) = 0.1
        _NormalScale    ("Normal Scale",    Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LifeColor;
                float  _LifeFraction;
                float4 _EraColor;
                float  _Smoothness;
                float  _NormalScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
                UNITY_FOG_COORDS(2)
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = vni.normalWS;
                OUT.color       = IN.color;
                UNITY_TRANSFER_FOG(OUT, OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Vertex color encodes height/biome data baked at generation time
                float biome = IN.color.r;

                // Blend barren ↔ life based on global life fraction + biome variation
                float blend = saturate(_LifeFraction + biome * 0.3 - 0.1);
                float3 surfaceColor = lerp(_BaseColor.rgb, _LifeColor.rgb, blend);
                surfaceColor *= _EraColor.rgb;

                Light mainLight = GetMainLight();
                float3 normal  = normalize(IN.normalWS);
                float  ndotl   = saturate(dot(normal, mainLight.direction));
                float3 ambient = SampleSH(normal);
                float3 lit = surfaceColor * (mainLight.color * ndotl + ambient);

                // Specular highlight
                float3 viewDir  = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 halfDir  = normalize(mainLight.direction + viewDir);
                float  spec     = pow(saturate(dot(normal, halfDir)), lerp(8, 256, _Smoothness));
                lit += mainLight.color * spec * _Smoothness * 0.5;

                half4 result = half4(saturate(lit), 1.0);
                UNITY_APPLY_FOG(IN.fogCoord, result);
                return result;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
