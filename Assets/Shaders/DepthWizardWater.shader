// DepthWizardWater.shader
//
// Flat, unlit, semi-transparent water color for the lake-edge plane
// (see DepthWizardSceneSetup.AddLakeWaterPlane). Deliberately as cheap as
// the plane it's painted on - no reflection, no waves, no fog - matching
// the brief's "cheapest fix" for the pinned-floor-elevation lake pixels.
// Not fog-aware: at the fog distances this project sets up, terrain fades
// into the sky color but this plane won't, so a lake very close to the fog
// distance could show a faint seam. Worth revisiting with a URP Lit/water
// shader later if that turns out to matter visually.
Shader "DepthWizard/Water"
{
    Properties
    {
        _Color("Water Color", Color) = (0.15, 0.35, 0.5, 0.75)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

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
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
