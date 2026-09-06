// DepthWizardGradientSky.shader
//
// Minimal three-color procedural skybox for URP: blends horizon -> sky above
// the horizon line and horizon -> ground below it, using the view direction
// only (no cubemap, no sun disc). Written by hand instead of using the
// legacy built-in "Skybox/Procedural" shader because that shader isn't
// URP-compatible and renders as a broken/magenta material under this
// project's Universal Render Pipeline.
//
// _HorizonColor is deliberately meant to match RenderSettings.fogColor
// (see DepthWizardSceneSetup.ConfigureSkyAndFog) - that's what makes fogged
// terrain fade into the sky at the horizon instead of showing a seam.
Shader "DepthWizard/GradientSky"
{
    Properties
    {
        _SkyColor("Sky Color", Color) = (0.35, 0.55, 0.85, 1)
        _HorizonColor("Horizon Color", Color) = (0.75, 0.8, 0.85, 1)
        _GroundColor("Ground Color", Color) = (0.3, 0.28, 0.25, 1)
        _HorizonHeight("Horizon Height", Range(-0.3, 0.3)) = 0.0
        _SkyExponent("Sky Blend Exponent", Range(0.1, 10)) = 1.5
        _GroundExponent("Ground Blend Exponent", Range(0.1, 10)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _SkyColor;
            half4 _HorizonColor;
            half4 _GroundColor;
            float _HorizonHeight;
            float _SkyExponent;
            float _GroundExponent;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // The skybox mesh is a unit cube centered on the camera, so
                // object-space position IS the view direction.
                OUT.viewDir = IN.positionOS.xyz;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float h = normalize(IN.viewDir).y - _HorizonHeight;
                half3 col;
                if (h >= 0)
                {
                    float t = saturate(pow(saturate(h * 2.0), _SkyExponent));
                    col = lerp(_HorizonColor.rgb, _SkyColor.rgb, t);
                }
                else
                {
                    float t = saturate(pow(saturate(-h * 2.0), _GroundExponent));
                    col = lerp(_HorizonColor.rgb, _GroundColor.rgb, t);
                }
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
