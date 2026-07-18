Shader "Custom/CreatureURP"
{
    // URP lit creature shader.
    // AgentController sets _BaseColor via MaterialPropertyBlock to each lineage's color.
    // _BackboneTint lets AtmosphereManager tint all creatures by biochemistry backbone.
    // _EraBrightness / _EraSaturation are driven by EraVolumeController for the mood arc.
    Properties
    {
        _BaseColor       ("Base Color",        Color)  = (1, 0.6, 0.2, 1)
        _BackboneTint    ("Backbone Tint",     Color)  = (1, 1, 1, 1)
        _EmissionColor   ("Emission",          Color)  = (0, 0, 0, 1)
        _EmissionStrength("Emission Strength", Range(0,3)) = 0.0
        _EraBrightness   ("Era Brightness",    Range(0.2,2.0)) = 1.0
        _EraSaturation   ("Era Saturation",    Range(0,2.0))   = 1.0
        _Smoothness      ("Smoothness",        Range(0,1))     = 0.3
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BackboneTint;
                float4 _EmissionColor;
                float  _EmissionStrength;
                float  _EraBrightness;
                float  _EraSaturation;
                float  _Smoothness;
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

            // Shift RGB saturation: s=0 → grayscale, s=1 → identity, s=2 → vivid
            float3 AdjustSaturation(float3 rgb, float s)
            {
                float lum = dot(rgb, float3(0.299, 0.587, 0.114));
                return lerp(float3(lum,lum,lum), rgb, s);
            }

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
                float3 baseRGB = _BaseColor.rgb * _BackboneTint.rgb;
                // Vertex color modulates (e.g. health gradient baked in by AgentController)
                baseRGB *= IN.color.rgb;

                // Era mood arc
                baseRGB = AdjustSaturation(baseRGB, _EraSaturation) * _EraBrightness;

                // Simple Lambert + ambient
                Light mainLight = GetMainLight();
                float3 normal = normalize(IN.normalWS);
                float  ndotl  = saturate(dot(normal, mainLight.direction));
                float3 diffuse = mainLight.color * ndotl;
                float3 ambient = SampleSH(normal);

                float3 lit = baseRGB * (diffuse + ambient);
                lit += _EmissionColor.rgb * _EmissionStrength;

                half4 result = half4(saturate(lit), _BaseColor.a);
                UNITY_APPLY_FOG(IN.fogCoord, result);
                return result;
            }
            ENDHLSL
        }

        // Shadow caster pass so creatures cast shadows
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
